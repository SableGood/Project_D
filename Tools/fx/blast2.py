# 무채색 폭발 (압력파 + 먼지 + 파편 위주). 사용: python blast2.py <out> <cell px> <seed>
import numpy as np, math, sys
from PIL import Image
out = sys.argv[1]; C = int(sys.argv[2]); seed = int(sys.argv[3]) if len(sys.argv) > 3 else 7
s = C / 64.0
F = 14; COLS = 7
rng = np.random.default_rng(seed)
FLASH=(238,234,222); HOT=(206,192,164); WARM=(164,146,120)
D1=(152,146,136); D2=(118,113,106); D3=(86,82,78); D4=(60,57,56); OUT=(33,20,26)
RINGC=(226,224,216); SHARD=(70,62,56); SHARD2=(104,94,84)
yy, xx = np.mgrid[0:C, 0:C]; cx = cy = C/2 - 0.5
ang = np.arctan2(yy-cy, xx-cx); dist = np.hypot(xx-cx, yy-cy)
R = C*0.44                         # 다 퍼졌을 때 반지름 (지름 ≈ 0.875C)
bayer = np.array([[0,8,2,10],[12,4,14,6],[3,11,1,9],[15,7,13,5]])/16.0
B = np.tile(bayer, (C//4, C//4))
npuff = int(10 + 6*s)
puffs = [(i*2*math.pi/npuff + rng.uniform(-0.3,0.3), rng.uniform(0.35, 0.85), rng.uniform(0.11,0.17)*C) for i in range(npuff)]
inner = [(rng.uniform(0,2*math.pi), rng.uniform(0,0.3), rng.uniform(0.12,0.18)*C) for _ in range(int(4+2*s))]
nsh = int(14 + 10*s)
shards = [(rng.uniform(0,2*math.pi), rng.uniform(0.55,1.15), rng.uniform(0.6,1.6), int(rng.integers(2, 3+int(s)))) for _ in range(nsh)]
sheet = Image.new('RGBA', (C*COLS, C*((F+COLS-1)//COLS)), (0,0,0,0))
for f in range(F):
    t = f/(F-1)
    img = np.zeros((C, C, 4), np.uint8)
    def put(m, col): img[m] = (*col, 255)
    # 1) 바닥 먼지 고리 (납작한 타원, 바깥으로 밀려남)
    if 1 <= f <= 11:
        st = (f-1)/10
        rr = R*(0.35 + 0.68*(1-(1-st)**2))
        ex = np.hypot((xx-cx)/rr, (yy-(cy+C*0.12))/(rr*0.38))
        band = (ex > 0.72) & (ex < 1.0)
        fade = B < np.clip(1.6*(1-st), 0, 1)
        put(band & fade, D3); put(band & (ex > 0.86) & fade, D4)
    # 2) 먼지 구름 (덩어리, 커지며 떠오르고 디더로 사라짐)
    if f >= 1:
        st = (f-1)/(F-2)
        rise = st*C*0.1
        sm = np.zeros((C,C), bool); hi = np.zeros((C,C), bool)
        grow = 1 - (1-min(1, st*1.6))**2
        for (a, rr, rad) in puffs:
            r0 = R*rr*(0.35 + 0.65*grow)
            px = cx + math.cos(a)*r0; py = cy + math.sin(a)*r0*0.75 - rise*(0.5+rr)
            rd = rad*(0.55 + 0.75*grow)
            d = np.hypot(xx-px, yy-py)
            sm |= d < rd; hi |= np.hypot(xx-px+rd*0.3, yy-py+rd*0.35) < rd*0.55
        for (a, rr, rad) in inner:
            px = cx + math.cos(a)*R*rr; py = cy + math.sin(a)*R*rr*0.7 - rise
            rd = rad*(0.6 + 0.6*grow)
            sm |= np.hypot(xx-px, yy-py) < rd
        fade = B < np.clip(2.2*(1-st)+0.05, 0, 1)
        put(sm & fade, D2)
        put(hi & sm & fade, D1)
        shade = np.clip((yy - (cy - C*0.02)) / (C*0.3), 0, 1)
        put(sm & fade & (B < shade*0.7), D3)
        if f >= 4:   # 압력에 밀려 가운데가 비어 감
            hole = dist < R*(0.12 + 0.05*(f-4)) * (1 + 0.15*np.sin(ang*4+f))
            img[hole & sm] = 0
        edge = sm & ~(np.roll(sm,1,0) & np.roll(sm,-1,0) & np.roll(sm,1,1) & np.roll(sm,-1,1)) & fade
        put(edge, D4 if f < 8 else D3)
    # 3) 섬광 + 아주 짧은 불 (채도 낮게)
    if f <= 3:
        Rf = R*(0.18 + 0.22*min(1, f/1.5)) * (1 - 0.25*max(0, f-2))
        n = dist/Rf + np.sin(ang*5+f)*0.08
        put(n < 1.0, WARM if f >= 2 else HOT)
        put(n < 0.75, HOT if f >= 2 else FLASH)
        if f <= 1: put(n < 0.45, FLASH)
        put((n >= 1.0) & (n < 1.0 + 1.3/Rf), OUT)
    # 3b) 압력 줄기 (바깥으로 쏘는 먼지 선)
    if 1 <= f <= 4:
        for k in range(int(10*s)):
            a = k*2*math.pi/int(10*s) + (k*1.7 % 0.4)
            r0 = R*(0.25 + 0.17*f); r1 = r0 + R*(0.18 + 0.06*(k % 3))
            for u in np.linspace(r0, r1, int((r1-r0)*1.2)+1):
                ix = int(round(cx + math.cos(a)*u)); iy = int(round(cy + math.sin(a)*u*0.8))
                if 0 <= ix < C and 0 <= iy < C: img[iy, ix] = (*(D1 if f < 3 else D2), 255)
    # 4) 압력파 고리 (밝은 회색, 빠르게 바깥으로, 두께 줄며 끊김)
    if f <= 5:
        Rr = R*(0.3 + 0.75*(f/5)**0.7)
        th = (2.2 - 0.3*f)*s
        ring = np.abs(dist - Rr) < th*0.5
        put(ring & (B < 0.95 - 0.13*f), RINGC)
        if f <= 3:
            haze = (dist < Rr - th*0.5) & (dist > Rr - th*2.5)
            put(haze & (B < 0.18), RINGC)
    # 5) 파편 (어두운 조각, 포물선)
    if f <= 10:
        for (a, sp, up, sz) in shards:
            tt = f*0.11
            r = R*sp*(0.15 + 1.1*(1-(1-min(1, tt*1.4))**2))
            px = cx + math.cos(a)*r
            py = cy + math.sin(a)*r*0.6 - (up*C*0.55*tt - C*0.9*tt*tt)
            if f > 7 and rng.random() < 0.35: continue
            ix, iy = int(round(px)), int(round(py))
            col = SHARD if (sz > 1 or f % 2) else SHARD2
            for dx in range(sz):
                for dy in range(sz):
                    if 0 <= ix+dx < C and 0 <= iy+dy < C: img[iy+dy, ix+dx] = (*col, 255)
    sheet.paste(Image.fromarray(img), ((f % COLS)*C, (f//COLS)*C))
sheet.save(out + '.png')
open(out + '.txt', 'w').write(f'{F} {COLS} {C} {C} 22 {C//2} {C//2} 0\n')
sc = max(1, 256//C)
prev = Image.new('RGBA', (sheet.width*sc, sheet.height*sc), (70,96,60,255)); prev.alpha_composite(sheet.resize((sheet.width*sc, sheet.height*sc), Image.NEAREST))
prev.save(out + '_preview.png')
