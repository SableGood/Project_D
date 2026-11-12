import numpy as np, json, sys
from PIL import Image
from scipy import ndimage as ndi
sys.path.insert(0, '/tmp/claude-0/w/vox')
from smooth_lib import polys, bridge, earclip
OUTLINE = (0x21, 0x14, 0x1A)
def load(k): return np.asarray(Image.open(f'n_{k}.png').convert('RGBA')).astype(int)
def bleed(img, alpha, n=4):
    img = img.copy(); filled = alpha.copy()
    for _ in range(n):
        grow = ndi.binary_dilation(filled) & ~filled
        for y, x in zip(*np.where(grow)):
            ys, xs = slice(max(0, y-1), y+2), slice(max(0, x-1), x+2)
            m = filled[ys, xs]; img[y, x, :3] = np.median(img[ys, xs][m][:, :3], 0); img[y, x, 3] = 255
        filled |= grow
    return img
def prep(k, strip_ring):
    a = load(k); alpha = a[..., 3] > 128
    L = a[..., :3].mean(2); dark = L < 28
    img = a.copy()
    if strip_ring:
        nb = ndi.binary_dilation(~alpha, structure=np.array([[0,1,0],[1,1,1],[0,1,0]]))
        ring = alpha & dark & nb; keep = alpha & ~ring
        tmp = img.copy(); tmp[~keep, 3] = 0; tmp = bleed(tmp, keep, 2); img[ring] = tmp[ring]; dark = dark & ~ring
    img[alpha & dark, :3] = OUTLINE
    img[~alpha, 3] = 0
    return bleed(img, alpha, 4).astype(np.uint8), alpha
V = ['left', 'right', 'top', 'bottom', 'front', 'back']
views = {k: prep(k, k in ('left', 'right')) for k in V}
a = load('left'); H, W = a.shape[:2]; alpha = a[..., 3] > 128

reg = np.full((H, W), '', dtype=object)
for y in range(H):
    for x in range(W):
        if not alpha[y, x]: continue
        if x < 31: r = 'stock'
        elif y <= 9 and 43 <= x <= 57: r = 'rsight'
        elif y <= 10 and 108 <= x <= 120: r = 'fsight'
        elif x >= 100: r = 'barrel'
        elif 47 <= x <= 53 and 19 <= y <= 26: r = 'trigger'
        elif 56 <= x <= 70 and y >= 21: r = 'mag'
        elif 75 <= x <= 87 and y >= 18: r = 'foregrip'
        elif x >= 60 and y <= 18: r = 'hand'
        elif x <= 48 and y >= 21: r = 'grip'
        elif y <= 15: r = 'upper'
        else: r = 'lower'
        reg[y, x] = r
depth = {'stock': 7, 'rsight': 3, 'fsight': 2, 'barrel': 2.6, 'trigger': 1.2, 'mag': 4, 'foregrip': 3.6, 'hand': 7, 'grip': 5, 'upper': 6, 'lower': 5.4}
group = {k: 'frame' for k in depth}; group['mag'] = 'mv_mag'; group['trigger'] = 'mv_trigger'
ZM = max(depth.values()) / 2

AW, AH = 256, 128
place = {'left': (0, 0), 'right': (0, 44), 'top': (0, 86), 'bottom': (0, 102), 'front': (140, 0), 'back': (160, 0)}
atlas = np.zeros((AH, AW, 4), np.uint8); bbox = {}
for k, (ox, oy) in place.items():
    img, al = views[k]; h, w = img.shape[:2]; atlas[oy:oy+h, ox:ox+w] = img
    ys, xs = np.where(al); bbox[k] = (xs.min(), xs.max()+1, ys.min(), ys.max()+1)
Image.fromarray(atlas).save('Rifle_M4.png')
lx0, lx1, ly0, ly1 = bbox['left']; X0, X1, Y0, Y1 = lx0, lx1, H - ly1, H - ly0
def proj(view, X, Y, Z):
    ox, oy = place[view]; c0, c1, r0, r1 = bbox[view]; w, h = c1-c0, r1-r0
    fx = (X - X0) / (X1 - X0); fy = (Y1 - Y) / (Y1 - Y0); fz = (Z + ZM) / (2*ZM)
    if view == 'left': u, v = X, H - Y
    elif view == 'right': u, v = c0 + (1-fx)*w, r0 + fy*h
    elif view == 'top': u, v = c0 + fx*w, r0 + fz*h
    elif view == 'bottom': u, v = c0 + fx*w, r0 + fz*h
    elif view == 'front': u, v = c0 + (1-fz)*w, r0 + fy*h
    else: u, v = c0 + fz*w, r0 + fy*h
    return (ox + u) / AW, 1 - (oy + v) / AH

out = {}   # group -> verts, uvs, tris
def add(g, pts, uvs, normal):
    d = out.setdefault(g, {'v': [], 'uv': [], 't': []}); base = len(d['v']) // 3
    p = [np.array(q, float) for q in pts]
    n = np.cross(p[1]-p[0], p[2]-p[0])
    order = [0, 1, 2] if np.dot(n, normal) > 0 else [0, 2, 1]
    for q, uv in zip(pts, uvs): d['v'] += [round(c, 3) for c in q]; d['uv'] += [round(c, 5) for c in uv]
    d['t'] += [base + i for i in order]
def quad(g, P, U, normal):
    add(g, [P[0], P[1], P[2]], [U[0], U[1], U[2]], normal); add(g, [P[0], P[2], P[3]], [U[0], U[2], U[3]], normal)
for name in depth:
    m = reg == name
    if name not in ('rsight', 'fsight', 'trigger'): m = ndi.binary_dilation(m, structure=np.ones((3, 3))) & alpha
    hz = depth[name] / 2; g = group[name]
    for outer, holes in polys(m, 0.6, W, H):
        ring = bridge(outer, holes); tris = earclip(ring)
        Wp = [(x, H - y) for x, y in ring]; n = len(Wp)
        A = sum(Wp[i][0]*Wp[(i+1) % n][1] - Wp[(i+1) % n][0]*Wp[i][1] for i in range(n))
        for t in tris:
            add(g, [(*Wp[i], hz) for i in t], [proj('left', *Wp[i], hz) for i in t], (0, 0, 1))
            add(g, [(*Wp[i], -hz) for i in t], [proj('right', *Wp[i], -hz) for i in t], (0, 0, -1))
        edges = set((tuple(ring[i]), tuple(ring[(i+1) % n])) for i in range(n))
        for i in range(n):
            j = (i+1) % n
            if (tuple(ring[j]), tuple(ring[i])) in edges: continue
            (px, py), (qx, qy) = Wp[i], Wp[j]; dx, dy = qx-px, qy-py
            if abs(dx) + abs(dy) < 1e-6: continue
            nx, ny = (dy, -dx) if A > 0 else (-dy, dx); ln = (nx*nx+ny*ny) ** 0.5; nx, ny = nx/ln, ny/ln
            view = 'top' if ny > 0.7 else 'bottom' if ny < -0.7 else ('front' if nx > 0 else 'back')
            P = [(px, py, hz), (qx, qy, hz), (qx, qy, -hz), (px, py, -hz)]
            quad(g, P, [proj(view, *q) for q in P], (nx, ny, 0))
gy, gx = np.where(reg == 'grip'); fy, fx = np.where(reg == 'foregrip')
pts = {'pt_muzzle': [135.5, H - 13, 0], 'pt_hand_r': [float(gx.mean()) + 1, H - float(gy.mean()) + 2, 0],
       'pt_hand_l': [float(fx.mean()), H - float(fy.mean()), 0], 'pt_eject': [52, H - 12, 3.5]}
my, mx = np.where(reg == 'mag')
pts['pt_charge'] = [35, H - 8, 0]
pts['pt_mag_top'] = [float(mx[my < my.min()+3].mean()), H - float(my.min()), 0]
pts['pt_mag_axis'] = [float(mx.mean()), H - float(my.mean()), 0]
json.dump({'groups': [{'name': k, **v} for k, v in out.items()], 'points': [{'name': k, 'p': v} for k, v in pts.items()],
           'magUp': [-0.25, 1, 0], 'magLen': float(my.max() - my.min() + 1)},
          open('Rifle_M4_mesh.json', 'w'), separators=(',', ':'))
print({k: (len(v['v'])//3, len(v['t'])//3) for k, v in out.items()}, pts)
# region preview
pv = np.zeros((H, W, 3), np.uint8); cols = {k: np.random.default_rng(i).integers(60, 255, 3) for i, k in enumerate(depth)}
for k in depth: pv[reg == k] = cols[k]
Image.fromarray(pv).resize((W*6, H*6), Image.NEAREST).save('regions.png')
