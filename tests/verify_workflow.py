"""Independent image/page identity checks for workflow output; stdlib only."""
import sys
from pathlib import Path
from verify import parse

directory = Path(sys.argv[1])
baseline = parse(directory / 'baseline.qpdl')
front = parse(directory / 'duplex-front.qpdl')
back = parse(directory / 'duplex-back.qpdl')
assert len(baseline) == 4 and len(front) == len(back) == 4
for actual, reference in zip(front, [baseline[0], baseline[3], baseline[0], baseline[3]]):
    assert actual[4] == reference[4] and actual[2] == 1
assert all(x == 255 for x in back[0][4]) and not back[0][3]
assert all(x == 255 for x in back[2][4]) and not back[2][3]
assert back[1][4] == back[3][4] == baseline[2][4]
copies = parse(directory / 'single-page-copies.qpdl')
assert len(copies) == 2 and copies[0][4] == copies[1][4] == baseline[1][4]
cancelled = parse(directory / 'cancel-after-page.qpdl')
assert len(cancelled) == 1 and cancelled[0][4] == baseline[0][4]
short = parse(directory / 'short-edge-back.qpdl')
assert len(short) == 2
assert short[0][4] != baseline[3][4] and short[1][4] != baseline[1][4]
# Rotation should keep the total amount of ink approximately constant despite dithering.
def ink(page):
    return sum(8 - byte.bit_count() for byte in page[4])
for rotated, original in zip(short, [baseline[3], baseline[1]]):
    assert abs(ink(rotated) - ink(original)) / max(1, ink(original)) < 0.02
print('PASS independent page identities, selected copies, blank duplex backs, rotation, and complete cancellation output.')
