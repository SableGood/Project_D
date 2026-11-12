import numpy as np
from PIL import Image, ImageDraw, ImageFont
from scipy import ndimage as ndi
R='/mnt/user-data/uploads/Project_D/Project_D/Assets/Resources/Image/TurretIcons/'
S=1024; ACC=(0xC6,0xE7,0xA7,255)
def make(name, out, ow=42):
    im=Image.open(R+name).convert('RGBA'); im=im.crop(im.getbbox())
    # fit inside S minus outline margin, nearest (pixel art)
    k=(S-2*ow-24)/max(im.size)
    im=im.resize((round(im.width*k),round(im.height*k)),Image.NEAREST)
    c=Image.new('RGBA',(S,S),(0,0,0,0))
    ox,oy=(S-im.width)//2,(S-im.height)//2
    c.alpha_composite(im,(ox,oy))
    a=np.array(c)[...,3]>40
    a=ndi.binary_fill_holes(a)
    yy,xx=np.mgrid[-ow:ow+1,-ow:ow+1]; disk=(xx*xx+yy*yy)<=ow*ow
    ring=ndi.binary_dilation(a,structure=disk)&~(np.array(c)[...,3]>40)
    o=np.zeros((S,S,4),np.uint8); o[ring]=ACC
    res=Image.fromarray(o); res.alpha_composite(c)
    res.save(out); return res
a=make('TR020.png','icon_TR020.png'); b=make('TR022.png','icon_TR022.png')
# preview on light & dark backgrounds + small sizes
sheet=Image.new('RGBA',(1160,620),(30,30,30,255))
light=Image.new('RGBA',(580,620),(225,225,220,255)); sheet.alpha_composite(light,(580,0))
f=ImageFont.truetype('/mnt/user-data/uploads/Project_D/Project_D/Assets/Resources/Fonts/Galmuri11-Bold.ttf',22)
d=ImageDraw.Draw(sheet)
for col,bgx in ((0,0),(1,580)):
    for i,im in enumerate((a,b)):
        x=bgx+20+i*280
        sheet.alpha_composite(im.resize((256,256),Image.LANCZOS),(x,20))
        for j,sz in enumerate((64,48,32,16)):
            sheet.alpha_composite(im.resize((sz,sz),Image.LANCZOS),(x+j*66,300+(64-sz)//2))
        d.text((x,380),['TR020','TR022'][i],font=f,fill=(120,120,120,255))
sheet.save('icon2_sheet.png')
