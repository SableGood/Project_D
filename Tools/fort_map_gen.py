"""Fort map generator: Fort_Field.png, Fort_Height.bytes (depth + raise), Fort_Layout.txt
World: origin = map centre, +x east, +z north. Units metres.
"""
import numpy as np, struct
from PIL import Image
from scipy import ndimage as ndi

Image.MAX_IMAGE_PIXELS = None
rng = np.random.default_rng(7)

W, H = 84.0, 60.0              # v159 56x40 -> 84x60 (바깥을 돌아다닐 수 있게 1.5배)
CELL = 0.25
GW, GH = int(W / CELL) + 1, int(H / CELL) + 1
PPM = 48                       # texture pixels per metre (v159 56 -> 48: 텍스처 4096 이하)
TW, TH = int(W * PPM), int(H * PPM)
MAXDEPTH = 0.8
CLIFF = 1.3                    # cliff height (m) (v159 2.6 -> 1.3)

# ---- layout ----
BASE_C = np.array([0.0, -4.0])
IN_HX, IN_HZ = 9.0, 6.5        # interior half size
RING = 2.6                     # ring thickness
GAP = 3.0                      # entrance width
WALL_LEN = GAP + 0.7
OUTCROPS = [(-20.0, 13.5, 2.6), (21.0, 12.0, 2.4), (-31.0, -4.5, 2.2), (26.0, -12.0, 2.4),
            (-10.5, 21.0, 1.6), (10.0, 22.5, 1.8), (-33.0, 13.0, 1.5), (34.0, 5.0, 1.6),
            (-24.0, -20.0, 2.0), (18.0, -22.0, 1.8)]
SPAWNS = [(-29.0, 19.5), (0.0, 21.0), (29.0, 19.5), (-31.0, -14.0), (31.0, -18.0)]


def coords(nx, nz):
    x = (np.arange(nx) / (nx - 1) - 0.5) * W
    z = (np.arange(nz) / (nz - 1) - 0.5) * H
    return np.meshgrid(x, z)          # rows = z ascending


def smooth_noise(shape, sigma, seed):
    r = np.random.default_rng(seed).standard_normal(shape)
    n = ndi.gaussian_filter(r, sigma, mode='wrap')
    return n / (n.std() + 1e-9)


def rrect_sd(X, Z, cx, cz, hx, hz, r):
    """signed distance to rounded rect (negative inside)"""
    qx = np.abs(X - cx) - (hx - r)
    qz = np.abs(Z - cz) - (hz - r)
    out = np.hypot(np.maximum(qx, 0), np.maximum(qz, 0))
    ins = np.minimum(np.maximum(qx, qz), 0)
    return out + ins - r


def fields(nx, nz, noise_scale):
    X, Z = coords(nx, nz)
    # land: rounded rect inset 3m with wobble
    wob = smooth_noise((nz, nx), 6 * noise_scale, 1) * 0.45 + smooth_noise((nz, nx), 2 * noise_scale, 2) * 0.12
    land_sd = rrect_sd(X, Z, 0, 0, W / 2 - 3.0, H / 2 - 3.0, 4.0) + wob
    # cliffs
    cwob = smooth_noise((nz, nx), 3 * noise_scale, 3) * 0.18
    ring_out = rrect_sd(X, Z, BASE_C[0], BASE_C[1], IN_HX + RING, IN_HZ + RING, 3.0)
    ring_in = rrect_sd(X, Z, BASE_C[0], BASE_C[1], IN_HX, IN_HZ, 1.5)
    ring = np.maximum(ring_out + cwob, -(ring_in - cwob * 0.3))   # inside ring band if < 0
    gap = (np.abs(X - BASE_C[0]) < GAP / 2) & (Z > BASE_C[1])
    ring = np.where(gap, np.maximum(ring, 0.5), ring)
    moat_sd = ring                     # v154 기지 둘레 = 해자 (물). 입구만 땅 다리
    cliff_sd = np.full_like(ring, 9.0)
    for i, (cx, cz, r) in enumerate(OUTCROPS):
        ang = np.arctan2(Z - cz, X - cx)
        rr = r * (1 + 0.22 * np.sin(3 * ang + i) + 0.12 * np.sin(5 * ang + 2 * i))
        sd = np.hypot(X - cx, Z - cz) - rr + cwob
        cliff_sd = np.minimum(cliff_sd, sd)
    return X, Z, land_sd, cliff_sd, moat_sd


# ================= grid =================
X, Z, land_sd, cliff_sd, moat_sd = fields(GW, GH, 1.0 / (W / CELL / 224))
depth = np.clip(land_sd / 1.2, 0, 1) * MAXDEPTH          # water outside the land rect
depth = np.maximum(depth, np.clip(-moat_sd / 0.7, 0, 1) * MAXDEPTH)   # v154 해자
raise_ = (cliff_sd < 0).astype(float)
raise_[depth > 0.01] = 0
# top variation
top = CLIFF + smooth_noise(raise_.shape, 4, 9) * 0.1
raise_h = raise_ * top
MAXRAISE = float(raise_h.max())
d8 = np.clip(np.round(depth / MAXDEPTH * 255), 0, 255).astype(np.uint8)
r8 = np.clip(np.round(raise_h / MAXRAISE * 255), 0, 255).astype(np.uint8)
with open('Fort_Height.bytes', 'wb') as f:
    f.write(struct.pack('<iifff', GW, GH, W, H, MAXDEPTH))
    f.write(d8.tobytes())
    f.write(struct.pack('<f', MAXRAISE))
    f.write(r8.tobytes())
print('grid', GW, GH, 'maxraise', MAXRAISE)

# sanity: entrance reachable? base interior land connected to outside only via gap
land = (d8 <= 3) & (r8 == 0)
lab, n = ndi.label(land)
ci = (int(round((BASE_C[1] + H / 2) / CELL)), int(round((BASE_C[0] + W / 2) / CELL)))
outside = (int(round((16 + H / 2) / CELL)), int(round((0 + W / 2) / CELL)))
print('base label', lab[ci], 'outside label', lab[outside], 'components', n)

# ================= texture =================

# NOTE: noise fields at texture resolution differ from grid noise -> recompute masks from upsampled grid instead
from PIL import Image as I
def up(a):
    return np.asarray(I.fromarray(a.astype(np.float32)).resize((TW, TH), I.BILINEAR))
depth_t = up(depth)[::-1]          # image rows top = north
cliffm = up(raise_)[::-1]
# grass from island
isl = np.asarray(I.open('/home/claude/work/Assets/Resources/Image/Map/Island_Field.png').convert('RGB'))
ih, iw = isl.shape[:2]
crop = isl[int(ih * 0.09):int(ih * 0.91), int(iw * 0.045):int(iw * 0.955)]
sc = (TW / crop.shape[1])
crop = np.asarray(I.fromarray(crop).resize((TW, int(crop.shape[0] * sc)), I.LANCZOS)).astype(float)
ch = crop.shape[0]
grass = np.zeros((TH, TW, 3))
grass[:ch] = crop
flip = crop[::-1, ::-1]
start = TH - ch
ov = ch - start
grass_low = np.zeros_like(grass); grass_low[start:] = flip
w = np.zeros((TH, 1, 1)); w[start:ch, 0, 0] = np.linspace(0, 1, ov); w[ch:] = 1
grass = grass * (1 - w) + grass_low * w

img = grass.copy()
# water colours (LUT from island)
lut_d = np.array([0.0, 0.06, 0.2, 0.5, 0.8, 1.0])
lut_c = np.array([[150, 132, 96], [118, 112, 92], [96, 140, 152], [92, 136, 147], [83, 123, 138], [74, 100, 119]], float)
dn = np.clip(depth_t / MAXDEPTH, 0, 1)
wc = np.stack([np.interp(dn, lut_d, lut_c[:, k]) for k in range(3)], -1)
wn = smooth_noise((TH, TW), 6, 11)[..., None] * 4 + smooth_noise((TH, TW), 1.2, 12)[..., None] * 3
wc = wc + wn
wm = np.clip(dn / 0.04, 0, 1)[..., None]
img = img * (1 - wm) + wc * wm

# packed-dirt floor inside base + trodden path at the entrance
Xt, Zt = coords(TW, TH); Zt = Zt[::-1]
inside = rrect_sd(Xt, Zt, BASE_C[0], BASE_C[1], IN_HX + 0.3, IN_HZ + 0.3, 1.5)
pathm = np.clip(1 - np.hypot((Xt - BASE_C[0]) / 2.2, np.maximum(0, (Zt - (BASE_C[1] + IN_HZ)) / 6.0)), 0, 1)
pathm = pathm * (Zt > BASE_C[1] + IN_HZ - 1)
dirt_m = np.clip(-inside / 2.0, 0, 1) * 0.35 + pathm * 0.45
dirt_m = np.clip(dirt_m + smooth_noise((TH, TW), 5, 21) * 0.08 * (dirt_m > 0.02), 0, 0.7)[..., None]
dirt = np.array([142, 124, 92]) + smooth_noise((TH, TW), 2, 22)[..., None] * 8
img = img * (1 - dirt_m) + dirt * dirt_m

# rock
n1 = smooth_noise((TH, TW), 14, 31); n2 = smooth_noise((TH, TW), 4, 32); n3 = smooth_noise((TH, TW), 1.0, 33)
rock = np.array([128, 118, 104]) + (n1 * 10 + n2 * 9 + n3 * 6)[..., None]
cracks = np.abs(smooth_noise((TH, TW), 6, 34)) < 0.06
rock[cracks] *= 0.72
# lighting: top-left highlight / bottom-right shade from cliff mask gradient
cm_s = ndi.gaussian_filter(cliffm, 10)
gy, gx = np.gradient(cm_s)
shade = (-gx * 1.0 + gy * 1.0) * 220     # light from north-west (image up-left)
rock = rock + np.clip(shade, -35, 35)[..., None]
# dark edge strip (side faces map here)
dist_in = ndi.distance_transform_edt(cliffm > 0.5)
edge = np.clip(1 - dist_in / (PPM * 0.28), 0, 1)
rock = rock * (1 - 0.45 * edge[..., None])
rm = np.clip((cliffm - 0.35) / 0.3, 0, 1)[..., None]
img = img * (1 - rm) + rock * rm
# soft contact shadow around cliffs on the ground (south-east)
sh = ndi.shift(ndi.gaussian_filter(cliffm, 18), (PPM * 0.6, PPM * 0.4), order=1)
img = img * (1 - 0.28 * np.clip(sh * (1 - rm[..., 0]), 0, 1))[..., None]

img = np.clip(img, 0, 255).astype(np.uint8)
I.fromarray(img).save('Fort_Field.png', optimize=True)

# layout
with open('Fort_Layout.txt', 'w', encoding='utf-8', newline='\n') as f:
    f.write('# 요새 맵 배치 (맵 가운데 기준 m, +x 동쪽, +z 북쪽). gen.py가 만듦\n')
    f.write(f'player {BASE_C[0]:.2f} {BASE_C[1]:.2f}\n')
    f.write(f'base {BASE_C[0]:.2f} {BASE_C[1]:.2f} {IN_HX:.2f} {IN_HZ:.2f}\n')
    wz = BASE_C[1] + IN_HZ + RING * 0.5
    f.write(f'wall {BASE_C[0]:.2f} {wz:.2f} 0 {WALL_LEN:.2f}\n')
    for s in SPAWNS:
        f.write(f'spawn {s[0]:.2f} {s[1]:.2f}\n')
print('done', TW, TH)
