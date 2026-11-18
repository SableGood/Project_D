import numpy as np, math, sys
from PIL import Image
out = sys.argv[1]
C = 64; F = 12; COLS = 6
rng = np.random.default_rng(7)
WHITE=(255,250,226); YEL=(255,214,74); ORA=(255,138,42); RED=(214,67,42); DRK=(90,38,30); OUT=(33,20,26)
SM1=(118,112,104); SM2=(84,80,76); SM3=(56,52,52)
yy, xx = np.mgrid[0:C, 0:C]; cx = cy = C/2 - 0.5
ang = np.arctan2(yy-cy, xx-cx); dist = np.hypot(xx-cx, yy-cy)
wob = lambda k, amp, seed: 1 + amp*(np.sin(k*ang+seed)*0.6 + np.sin((k+3)*ang+seed*2)*0.4)
sparks = [(rng.uniform(0, 2*math.pi), rng.uniform(0.7,1.25), rng.integers(0,2)) for _ in range(18)]
puffs = [(i*2*math.pi/11 + rng.uniform(-0.25,0.25), rng.uniform(0.6, 0.9), rng.uniform(4.5,6.5)) for i in range(11)]
sheet = Image.new('RGBA', (C*COLS, C*((F+COLS-1)//COLS)), (0,0,0,0))
bayer = np.array([[0,8,2,10],[12,4,14,6],[3,11,1,9],[15,7,13,5]])/16.0
B = np.tile(bayer, (C//4, C//4))
for f in range(F):
    t = f/(F-1)
    img = np.zeros((C, C, 4), np.uint8)
    def put(mask, col):
        img[mask] = (*col, 255)
    # 1) 연기 (3프레임부터, 위로 떠오르며 디더로 사라짐)
    if f >= 3:
        st = (f-3)/(F-4)
        rise = st*6
        sm = np.zeros((C,C), bool); sm2 = np.zeros((C,C), bool)
        for (a, rr, s) in puffs:
            r0 = 26*rr*(0.8+0.35*st)
            px = cx + math.cos(a)*r0; py = cy + math.sin(a)*r0*0.8 - rise
            rad = s*(1+0.6*st)
            d = np.hypot(xx-px, yy-py)
            sm |= d < rad; sm2 |= d < rad*0.6
        fade = B < np.clip(2.3*(1 - st), 0, 1)
        put(sm & fade, SM2); put(sm2 & fade & (yy < cy), SM1)
        edge = sm & ~np.roll(sm,1,0) & fade
        put(edge, SM3)
    # 2) 불덩이
    if f <= 6:
        g = min(1, f/2.5) if f < 3 else 1 - (f-3)/4.5
        R = 4 + 24*min(1, f/3) ** 0.7
        R = R * (1 if f < 4 else 1 - (f-3)*0.12)
        w = wob(5, 0.12, f*0.7)
        tex = np.sin(xx*0.9+f)*np.cos(yy*0.8-f)*0.06
        n = dist / (R*w) + tex
        hole = (f >= 5) & (n < 0.35 + 0.15*(f-5))
        put((n < 1.0) & ~hole, DRK if f >= 4 else RED)
        put((n < 0.86) & ~hole, RED if f >= 5 else ORA)
        put((n < 0.68) & ~hole, ORA if f >= 5 else YEL)
        if f <= 4: put((n < 0.45) & ~hole, YEL if f >= 3 else WHITE)
        if f <= 2: put(n < 0.28, WHITE)
        ring = (n >= 1.0) & (n < 1.0 + 1.6/R*1.0) 
        put(ring, OUT)
    # 3) 충격파 고리
    if 1 <= f <= 4:
        Rr = 10 + f*5
        put((np.abs(dist - Rr) < 0.7) & (B < 0.75), (255,236,190))
    # 4) 불꽃 조각
    if f <= 8:
        for (a, sp, big) in sparks:
            r = (6 + f*3.4) * sp
            if r > 31: continue
            px = int(round(cx + math.cos(a)*r)); py = int(round(cy + math.sin(a)*r + f*f*0.06))
            col = WHITE if f < 2 else YEL if f < 5 else ORA
            for dx in range(1+big):
                for dy in range(1+big):
                    if 0 <= px+dx < C and 0 <= py+dy < C: img[py+dy, px+dx] = (*col, 255)
            # 꼬리
            tx = int(round(cx + math.cos(a)*(r-2.5))); ty = int(round(cy + math.sin(a)*(r-2.5)))
            if f < 6 and 0 <= tx < C and 0 <= ty < C and img[ty,tx,3] == 0: img[ty,tx] = (*ORA, 255)
    sheet.paste(Image.fromarray(img), ((f % COLS)*C, (f//COLS)*C))
sheet.save(out + '.png')
open(out + '.txt', 'w').write(f'{F} {COLS} {C} {C} 24 {C//2} {C//2} 0\n')
prev = Image.new('RGBA', (sheet.width*4, sheet.height*4), (40,44,40,255)); prev.alpha_composite(sheet.resize((sheet.width*4, sheet.height*4), Image.NEAREST))
prev.save(out + '_preview.png')
