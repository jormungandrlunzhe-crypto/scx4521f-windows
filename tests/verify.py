"""Independent decoder and QPDL record checks; Python 3 stdlib only.
Usage: python tests/verify.py fixture_directory job.qpdl ...
"""
import struct
import sys
from pathlib import Path

def decode(data):
    raster = bytearray()
    i = 0
    while len(raster) < 620 * 128:
        a = data[i]
        i += 1
        if 0x80 <= a <= 0xBF:
            length = (((a & 0x3F) << 8) | data[i]) + 1
            i += 1
            raster.extend(data[i:i+length])
            i += length
        elif a >= 0xC0:
            length = 65537 - ((a << 8) | data[i])
            i += 1
            raster.extend(bytes([data[i]]) * length)
            i += 1
        elif 0x40 <= a <= 0x7F:
            length = 129 - a
            raster.extend(bytes([data[i]]) * length)
            i += 1
        else:
            raise AssertionError(f'Unexpected 0x0E control {a:02x}')
        assert len(raster) <= 620 * 128, 'Decoded band overflow'
    assert len(data) % 4 == 0
    assert len(data) - i <= 3 and not any(data[i:]), 'Invalid alignment padding'
    return raster

def decode_d(data):
    raster = bytearray([255]) * (620 * 128)
    i = 0
    pen = 0
    while i < len(data):
        if len(data)-i <= 4 and not any(data[i:]):
            break
        a = data[i]
        if a < 0x80:
            run = a & 63
            dy = a >> 6
            dx = int.from_bytes(data[i+1:i+2], 'big', signed=True)
            offset = dy * 4960 + dx
            i += 2
        elif a < 0xC0:
            dx = ((a & 63) << 8) | data[i+1]
            if dx & 0x2000:
                dx -= 0x4000
            dy = (data[i+2] >> 4) & 3
            run = ((data[i+2] & 15) << 8) | data[i+3]
            offset = dy * 4960 + dx
            i += 4
        else:
            assert a == 0xC0
            offset = int.from_bytes(data[i+1:i+4], 'big')
            run = ((data[i+4] & 63) << 8) | data[i+5]
            i += 6
        pen += offset
        assert run > 0 and 0 <= pen < 4960*128 and pen+run <= 4960*128
        for pixel in range(pen, pen+run):
            raster[pixel//8] &= 255 ^ (128 >> (pixel & 7))
    assert len(data) % 4 == 0 and 1 <= len(data)-i <= 4 and not any(data[i:])
    return raster

def parse(path):
    job = Path(path).read_bytes()
    assert job.startswith(b'\x1b%-12345X')
    marker = b'@PJL ENTER LANGUAGE = QPDL\n'
    start = job.index(marker) + len(marker)
    assert job.endswith(b'\t\x1b%-12345X')
    end = len(job) - len(b'\t\x1b%-12345X')
    i = start
    pages = []
    while i < end:
        header = job[i:i+17]
        assert len(header) == 17 and header[0] == 0 and header[1] == header[16] == 6
        copies, = struct.unpack('>H', header[2:4])
        width, height = struct.unpack('>HH', header[5:9])
        assert header[9:16] == bytes([1,0,1,0,2,1,1])
        i += 17
        bands = []
        raster = bytearray([255]) * (620 * ((height + 127) // 128) * 128)
        previous = -1
        while job[i] == 12:
            number = job[i+1]
            bw, bh = struct.unpack('>HH', job[i+2:i+6])
            assert number > previous and bw == width and bh == 128 and job[i+6] in (13,14)
            previous = number
            size, = struct.unpack('>I', job[i+7:i+11])
            assert size >= 4 and number * 128 < height
            data = job[i+11:i+11+size-4]
            checksum, = struct.unpack('>I', job[i+11+size-4:i+11+size])
            assert checksum == sum(data) & 0xFFFFFFFF
            bitmap = decode_d(data) if job[i+6] == 13 else decode(data)
            raster[number*128*620:(number+1)*128*620] = bitmap
            bands.append(number)
            i += 11 + size
        assert job[i:i+3] == bytes([1]) + struct.pack('>H', copies)
        i += 3
        pages.append((width,height,copies,bands,raster[:height*620]))
    assert i == end and pages
    return pages

def main():
    directory = Path(sys.argv[1])
    for raw in sorted(directory.glob('*.raw')):
        encoded = raw.with_suffix('.encoded').read_bytes()
        assert decode(encoded) == raw.read_bytes(), raw.name
        print(f'PASS codec roundtrip {raw.name} ({len(encoded)} bytes)')
        encoded_d = raw.with_suffix('.encoded-d')
        if encoded_d.exists():
            assert decode_d(encoded_d.read_bytes()) == raw.read_bytes(), raw.name
            print(f'PASS 0x0D roundtrip {raw.name} ({encoded_d.stat().st_size} bytes)')
    for job in sys.argv[2:]:
        pages = parse(job)
        if Path(job).name == 'pdf.qpdl':
            assert len(pages) == 2 and all(p[2] == 1 for p in pages)
        if Path(job).name == 'tiff.qpdl':
            assert len(pages) == 2 and all(p[2] == 2 for p in pages)
        if Path(job).name == 'image.qpdl':
            assert len(pages) == 1
        print(f'PASS QPDL {Path(job).name}: {len(pages)} pages; bands {[len(p[3]) for p in pages]}; copies {[p[2] for p in pages]}')
        if len(pages) == 1 and Path(job).name == 'test-a4.qpdl':
            w,h,c,b,r = pages[0]
            assert (w,h,c) == (4960,6892,1)
            # Protocol raster diagnostic in standard PBM. Convert to PNG with PIL if available.
            destination = directory / 'test-page.pbm'
            destination.write_bytes(f'P4\n4960 {h}\n'.encode() + bytes(x ^ 255 for x in r))
    print('All independent checks passed.')

if __name__ == '__main__':
    main()
