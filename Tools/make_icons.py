import numpy as np
from PIL import Image, ImageDraw, ImageFilter
R='/mnt/user-data/uploads/Project_D/Project_D/Assets/Resources/Image/'
colt=Image.open(R+'Weapons/Pic_Colt.png').convert('RGBA')
tr10=Image.open(R+'TurretIcons/TR010.png').convert('RGBA')
tr20=Image.open(R+'TurretIcons/TR020.png').convert('RGBA')
S=1024
def hexc(h,a=255): return (int(h[1:3],16),int(h[3:5],16),int(h[5:7],16),a)
OUT=hexc('#21141A'); ACC=hexc('#C6E7A7')
def crop(im): return im.crop(im.getbbox())
def scaled(im, w):
    im=crop(im); h=round(im.height*w/im.width)
    return im.resize((w,h), Image.NEAREST)
def outline(im, px, col=OUT):
    a=np.array(im)[...,3]>40
    from scipy import ndimage as ndi
    ring=ndi.binary_dilation(a,iterations=px)&~a
    o=Image.new('RGBA',im.size,(0,0,0,0)); oa=np.array(o); oa[ring]=col; o=Image.fromarray(oa)
    o.alpha_composite(im); return o
def pad(im,p):
    c=Image.new('RGBA',(im.width+2*p,im.height+2*p),(0,0,0,0)); c.alpha_composite(im,(p,p)); return c
def base(top,bot,ring=ACC):
    img=Image.new('RGBA',(S,S),(0,0,0,0))
    g=np.zeros((S,S,4),np.uint8)
    t=np.linspace(0,1,S)[:,None]
    for k in range(3): g[...,k]=(top[k]*(1-t)+bot[k]*t).astype(np.uint8)
    g[...,3]=255
    grad=Image.fromarray(g)
    mask=Image.new('L',(S,S),0); ImageDraw.Draw(mask).rounded_rectangle([24,24,S-25,S-25],radius=190,fill=255)
    img.paste(grad,(0,0),mask)
    d=ImageDraw.Draw(img)
    d.rounded_rectangle([24,24,S-25,S-25],radius=190,outline=OUT,width=28)
    d.rounded_rectangle([58,58,S-59,S-59],radius=158,outline=ring,width=8)
    return img
def shadow(im, off=(18,22), blur=0, alpha=110):
    a=im.split()[3]
    sh=Image.new('RGBA',im.size,(0,0,0,0)); sh.putalpha(a.point(lambda v: alpha if v>40 else 0))
    c=Image.new('RGBA',(im.width+off[0],im.height+off[1]),(0,0,0,0)); c.alpha_composite(sh,off); c.alpha_composite(im); return c
def place(img, im, cx, cy):
    img.alpha_composite(im,(int(cx-im.width/2),int(cy-im.height/2)))

# A: Colt + crosshair on dark
a=base(hexc('#6B7360'),hexc('#3E443A'))
d=ImageDraw.Draw(a)
cx,cy=S//2,S//2+10
for r,w in ((300,14),(210,8)):
    d.ellipse([cx-r,cy-r,cx+r,cy+r],outline=ACC[:3]+(210,),width=w)
for ang in range(4):
    dx,dy=[(1,0),(-1,0),(0,1),(0,-1)][ang]
    d.line([cx+dx*230,cy+dy*230,cx+dx*350,cy+dy*350],fill=ACC[:3]+(210,),width=14)
g=outline(pad(scaled(colt,720),16),10)
place(a,shadow(g),S//2+10,S//2+20)
a.save('icon_A_colt_crosshair.png')

# B: turret inside moat ring
b=base(hexc('#5E8C4A'),hexc('#3F6A36'))
d=ImageDraw.Draw(b)
d.ellipse([140,170,S-140,S-110],fill=hexc('#4E7E9A'),outline=OUT,width=10)
d.ellipse([205,235,S-205,S-175],fill=hexc('#8E7C5C'),outline=OUT,width=8)
t=outline(pad(scaled(tr10,640),14),9)
place(b,t,S//2,S//2+10)
b.save('icon_B_turret_moat.png')

# C: turret + colt crossed
c=base(hexc('#3E4A5E'),hexc('#262C38'),ring=hexc('#8EC5E8'))
t2=outline(pad(scaled(tr20,600),14),9)
place(c,t2,S//2+90,S//2-90)
g2=outline(pad(scaled(colt,560),14),9)
place(c,shadow(g2),S//2-110,S//2+170)
c.save('icon_C_turret_colt.png')

# D: Colt firing on warm bg with pixel flash
dd=base(hexc('#F2B66D'),hexc('#D9813A'),ring=hexc('#FFF1C8'))
dr=ImageDraw.Draw(dd)
# speed lines
for i,y in enumerate(range(300,760,70)):
    dr.rectangle([120+(i%3)*30, y, 120+(i%3)*30+150-(i%2)*50, y+14], fill=hexc('#FFE7B8',200))
import math
cc=crop(colt); small=cc.resize((cc.width//3, cc.height//3), Image.NEAREST)
w0,h0=small.size; ANG=18
rot=small.rotate(ANG, resample=Image.NEAREST, expand=True)
# muzzle (right-top of the original) after rotation
px,py=w0-1-w0/2, h0*0.10-h0/2
th=math.radians(ANG)
rx,ry=px*math.cos(th)+py*math.sin(th), -px*math.sin(th)+py*math.cos(th)
mx,my=rx+rot.width/2, ry+rot.height/2
K=5
rot=rot.resize((rot.width*K, rot.height*K), Image.NEAREST)
rot=outline(pad(rot,16),10)
gx,gy=S//2-70, S//2+90
sh=shadow(rot,(14,18))
place(dd, sh, gx, gy)
ox,oy=gx-sh.width/2+16+mx*K, gy-sh.height/2+16+my*K
P=30
dirx,diry=math.cos(th),-math.sin(th)
fx,fy=ox+dirx*P*3.2, oy+diry*P*3.2
star=[]
for x in range(-4,5):
    for y in range(-4,5):
        r=abs(x)+abs(y)
        if r<=2 or (y==0 and abs(x)<=4) or (x==0 and abs(y)<=4) or (abs(x)==abs(y) and abs(x)<=3): star.append((x,y))
for (x,y) in sorted(star,key=lambda q:-(abs(q[0])+abs(q[1]))):
    r=max(abs(x),abs(y))
    col=hexc('#FFFFFF') if r==0 else hexc('#FFF3A0') if r<=1 else hexc('#FFC24A') if r<=2 else hexc('#F07A2A')
    cx_,cy_=fx+x*P, fy+y*P
    dr.rectangle([cx_-P//2, cy_-P//2, cx_+P//2, cy_+P//2], fill=col, outline=OUT, width=4)
dd.save('icon_D_colt_firing.png')

# contact sheet with small-size previews
names=['icon_A_colt_crosshair.png','icon_B_turret_moat.png','icon_C_turret_colt.png','icon_D_colt_firing.png']
sheet=Image.new('RGBA',(4*300+50,420),hexc('#1E1E1E'))
from PIL import ImageFont
f=ImageFont.truetype('/mnt/user-data/uploads/Project_D/Project_D/Assets/Resources/Fonts/Galmuri11-Bold.ttf',22)
sd=ImageDraw.Draw(sheet)
for i,n in enumerate(names):
    im=Image.open(n)
    sheet.alpha_composite(im.resize((256,256),Image.LANCZOS),(25+i*300,20))
    for j,sz in enumerate((64,48,32,16)):
        sheet.alpha_composite(im.resize((sz,sz),Image.LANCZOS),(25+i*300+j*70 - (0), 300+(64-sz)//2))
    sd.text((25+i*300, 385), 'ABCD'[i], font=f, fill=(230,230,220,255))
sheet.save('icons_sheet.png')
