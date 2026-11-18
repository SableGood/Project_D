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
# v172 1m 타일에 맞춤: 칸 가운데 = 정수 좌표, 칸 경계 = .5 (BuildGrid). 절벽·해자·벽 자리 모두 칸 경계에 딱 맞게
# v171 4안: 요새(C)를 남쪽 가운데로 (해자가 바깥 물과 이어짐), 거점 A·B·D·E는 아래쪽 반원, 몹 스폰은 위쪽 10시~2시
BASE_C = np.array([0.0, -19.5])
IN_HX, IN_HZ = 6.5, 5.0        # 안쪽 = 칸 x -6..6, z -24..-15
RING = 2.6                     # ring thickness
GAP = 3.0                      # entrance width (칸 x -1..1)
WALL_LEN = 3.0
OUTCROPS = [(-20.0, 13.5, 2.6), (21.0, 12.0, 2.4), (-31.0, -4.5, 2.2), (26.0, -12.0, 2.4),
            (-10.5, 21.0, 1.6), (10.0, 22.5, 1.8), (-33.0, 13.0, 1.5), (34.0, 5.0, 1.6),
            (-24.0, -20.0, 2.0), (18.0, -22.0, 1.8)]
SPAWNS = [(-37.0, 18.0), (-17.0, 25.5), (0.0, 26.0), (17.0, 25.5), (37.0, 18.0)]   # 위쪽 10시~2시
# 거점 (이름, 가운데 x z, 반가로, 반세로) — 칸 경계 기준 바깥 둘레
ZONES = [('A', -31.0, 4.5, 6.5, 6.0), ('B', -32.0, -17.5, 5.5, 7.0), ('C', 0.0, -19.5, 6.5, 5.0),
         ('D', 28.0, -20.5, 5.5, 4.0), ('E', 31.5, 3.0, 5.0, 5.5)]
# 거점 입구 벽 자리 (거점, x, z, 각도, 길이) — 0 = 동서로 놓인 벽, 90 = 남북. 입구는 3칸
SLOTS = [('A', -32.0, 10.0, 0, 3.0), ('A', -25.0, 4.0, 90, 3.0), ('B', -33.0, -11.0, 0, 3.0), ('B', -27.0, -15.0, 90, 3.0),
         ('D', 29.0, -17.0, 0, 3.0), ('E', 27.0, 3.0, 90, 3.0)]
# 새 절벽: 칸 범위 (x0, z0, x1, z1) 양끝 포함 — 1칸 두께, 입구 3칸은 비움
RECTS = [
    # A 서쪽 협곡: 칸 x -37..-25, z -1..10 / 입구 북(x -33..-31) · 동(z 3..5)
    (-37, 10, -34, 10), (-30, 10, -25, 10), (-25, 6, -25, 9), (-25, -1, -25, 2), (-37, -1, -26, -1), (-37, 0, -37, 9),
    # B 남서 폐허: 칸 x -37..-27, z -24..-11 / 입구 북(x -34..-32) · 동(z -16..-14)
    (-37, -11, -35, -11), (-31, -11, -27, -11), (-27, -13, -27, -12), (-27, -24, -27, -17), (-36, -24, -28, -24), (-37, -24, -37, -12),
    # D 남동 바위굴: 칸 x 23..33, z -24..-17 / 입구 북(x 28..30)
    (23, -17, 27, -17), (31, -17, 33, -17), (33, -24, 33, -18), (23, -24, 32, -24), (23, -23, 23, -18),
    # E 동쪽 바위 요새: 칸 x 27..36, z -2..8 / 입구 서(z 2..4)
    (27, 5, 27, 8), (27, -2, 27, 1), (28, 8, 36, 8), (28, -2, 36, -2), (36, -1, 36, 7),
]


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
    # v171 해자 남쪽을 맵 밖까지 늘려 바깥 물과 이어지게
    ring_out = rrect_sd(X, Z, BASE_C[0], BASE_C[1] - 3.0, IN_HX + RING, IN_HZ + RING + 3.0, 3.0)
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
    # v172 거점 둘레 새 절벽: 칸 경계에 딱 맞는 네모 (흔들림 없음)
    for (x0, z0, x1, z1) in RECTS:
        cx, cz = (x0 + x1) / 2, (z0 + z1) / 2
        hx, hz = (abs(x1 - x0) + 1) / 2, (abs(z1 - z0) + 1) / 2
        cliff_sd = np.minimum(cliff_sd, rrect_sd(X, Z, cx, cz, hx, hz, 0.0))
    return X, Z, land_sd, cliff_sd, moat_sd


def seg_sd(X, Z, x1, z1, x2, z2, t):
    px, pz, dx, dz = X - x1, Z - z1, x2 - x1, z2 - z1
    h = np.clip((px * dx + pz * dz) / (dx * dx + dz * dz), 0, 1)
    return np.hypot(px - dx * h, pz - dz * h) - t


# ================= grid =================
X, Z, land_sd, cliff_sd, moat_sd = fields(GW, GH, 1.0 / (W / CELL / 224))
depth = np.clip(land_sd / 1.2, 0, 1) * MAXDEPTH          # water outside the land rect
depth = np.maximum(depth, np.clip(-moat_sd / 0.7, 0, 1) * MAXDEPTH)   # v154 해자
raise_ = (cliff_sd < 0).astype(float)
raise_[depth > 0.01] = 0

# v175 칸에 맞추기: 1m 칸마다 물 / 절벽 / 땅 중 하나로 (칸 가운데 값으로 정함). 바위·해안·해자 모두 칸 경계에 딱 맞음
#  격자 점이 칸 경계 위에 있으면 닿은 칸이 모두 같은 종류일 때만 그 종류 → 이웃 땅 칸은 깨끗하게 남음
def tile_index(v):
    a = np.ceil(v - 0.5 - 1e-6).astype(int); b = np.floor(v + 0.5 + 1e-6).astype(int)
    return a, b
TXS = np.arange(-int(W / 2), int(W / 2) + 1); TZS = np.arange(-int(H / 2), int(H / 2) + 1)
def at(arr, x, z):
    return arr[int(round((z + H / 2) / CELL)), int(round((x + W / 2) / CELL))]
water_t = np.array([[at(depth, x, z) > 0.01 for x in TXS] for z in TZS])
cliff_t = np.array([[at(raise_, x, z) > 0 and not at(depth, x, z) > 0.01 for x in TXS] for z in TZS])
def all_touch(T, outside):
    ax, bx = tile_index(X[0, :]); az, bz = tile_index(Z[:, 0])
    def get(zi, xi):
        zz = zi[:, None] - TZS[0]; xx = xi[None, :] - TXS[0]
        ok = (zz >= 0) & (zz < len(TZS)) & (xx >= 0) & (xx < len(TXS))
        v = np.full(ok.shape, outside)
        v[ok] = T[np.clip(zz, 0, len(TZS) - 1), np.clip(xx, 0, len(TXS) - 1)][ok]
        return v
    return get(az, ax) & get(az, bx) & get(bz, ax) & get(bz, bx)
water_pt = all_touch(water_t, True)
cliff_pt = all_touch(cliff_t, False)
depth = np.where(water_pt, np.maximum(depth, 0.3 * MAXDEPTH), 0.0)
raise_ = cliff_pt.astype(float)
raise_[water_pt] = 0
print('tiles: water', int(water_t.sum()), 'cliff', int(cliff_t.sum()), 'land', int((~water_t & ~cliff_t).sum()))
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
    wz = np.floor(BASE_C[1] + IN_HZ + RING * 0.5 + 0.5)   # v172 칸 가운데 (z -13)
    f.write(f'wall {BASE_C[0]:.2f} {wz:.2f} 0 {WALL_LEN:.2f}\n')
    for s in SPAWNS:
        f.write(f'spawn {s[0]:.2f} {s[1]:.2f}\n')
    f.write('# v172 거점 범위(zone)·입구 자리(slot, 참고용). 벽은 플레이어가 X로 타일 위에 지음\n')
    for z in ZONES:
        f.write(f'zone {z[0]} {z[1]:.2f} {z[2]:.2f} {z[3]:.2f} {z[4]:.2f}\n')
    for s in SLOTS:
        f.write(f'slot {s[0]} {s[1]:.2f} {s[2]:.2f} {s[3]} {s[4]:.2f}\n')
print('done', TW, TH)
