import numpy as np
from PIL import Image
a=np.asarray(Image.open('sheet.png').convert('RGB')).astype(float)
views={'left':(21,26,958,292),'front':(1098,28,102,347),'right':(24,304,955,269),'back':(1105,487,86,312),'top':(24,579,948,93),'bottom':(24,727,948,80)}
def native(crop,rng):
    H,W=crop.shape[:2]; best=None
    for s in rng:
        for ox in np.arange(0,s,1.0):
            for oy in np.arange(0,s,1.0):
                nx=int((W-ox)/s); ny=int((H-oy)/s)
                xs=(ox+(np.arange(nx)+0.5)*s).astype(int); ys=(oy+(np.arange(ny)+0.5)*s).astype(int)
                small=crop[ys][:,xs]
                xi=np.clip(((np.arange(W)-ox)/s).astype(int),0,nx-1); yi=np.clip(((np.arange(H)-oy)/s).astype(int),0,ny-1)
                err=np.abs(small[yi][:,xi]-crop).mean()
                if best is None or err<best[0]: best=(err,s,ox,oy,small)
    return best
import sys
rng=np.arange(float(sys.argv[1]),float(sys.argv[2])+0.001,float(sys.argv[3]))
for k,(x,y,w,h) in views.items():
    pad=6
    crop=a[max(0,y-pad):y+h+pad, x-pad:x+w+pad]
    err,s,ox,oy,small=native(crop,rng)
    rgb=small.astype(np.uint8)
    white=(np.abs(small-255).sum(2)<60)
    rgba=np.dstack([rgb, np.where(white,0,255).astype(np.uint8)])
    Image.fromarray(rgba).save(f'n_{k}.png')
    Image.fromarray(rgba).resize((rgba.shape[1]*6,rgba.shape[0]*6),Image.NEAREST).save(f'n_{k}_x6.png')
    print(k, round(err,1), round(s,2), rgba.shape[:2])
