from PIL import Image, ImageDraw, ImageFont
A='/mnt/user-data/attachments/5ab61267-6f04-574e-b1c9-ce0c546f111a/'
F='/mnt/user-data/uploads/Project_D/Project_D/Assets/Resources/Fonts/'
s1=Image.open(A+'aac19837-image.png').convert('RGB'); s2=Image.open(A+'e4277090-image.png').convert('RGB')
s3=Image.open(A+'7fd30c6e-image.png').convert('RGB'); s4=Image.open(A+'6059389f-image.png').convert('RGB')
def font(sz,b=False): return ImageFont.truetype(F+('Galmuri11-Bold.ttf' if b else 'Galmuri11.ttf'), sz)
def hx(h): return tuple(int(h[i:i+2],16) for i in (1,3,5))
BG=hx('#2B2B29'); PANEL=hx('#3A3A3A'); LINE=hx('#4C4C4A'); ACC=hx('#C6E7A7'); TXT=hx('#EDEDE6'); SUB=hx('#A8A8A0'); OUT=hx('#21141A'); ORG=hx('#F2B66D')
W,H=1920,1080
img=Image.new('RGB',(W,H),BG); d=ImageDraw.Draw(img)

# header band
d.rectangle([0,0,W,150],fill=PANEL); d.rectangle([0,150,W,154],fill=ACC); d.rectangle([0,155,W,162],fill=hx('#232321'))
d.text((60,30),'Project_D',font=font(72,True),fill=TXT)
d.text((470,52),'탑뷰 + 1인칭 생존 디펜스 슈터',font=font(32,True),fill=ACC)
d.text((470,98),'포탑을 세우고 입구 벽을 지키며, 몰려오는 몹들 사이에서 최대한 오래 버텨라',font=font(22),fill=SUB)
tag='Windows 프로토타입 v161'
tw=d.textlength(tag,font=font(22)); d.text((W-60-tw,60),tag,font=font(22),fill=SUB)

def framed(im, box, label=None):
    x0,y0,x1,y1=box
    d.rectangle([x0-4,y0-4,x1+3,y1+3],fill=OUT)
    d.rectangle([x0-2,y0-2,x1+1,y1+1],fill=LINE)
    img.paste(im.resize((x1-x0,y1-y0),Image.LANCZOS),(x0,y0))
    if label:
        f=font(20,True); tw=d.textlength(label,font=f)
        d.rectangle([x0,y1-38,x0+tw+28,y1],fill=OUT); d.text((x0+14,y1-31),label,font=f,fill=ACC)

# main screenshot (top-down, without the test panel)
main=s1.crop((440,0,1820,830))
mw=1000; mh=round(mw*main.height/main.width)
framed(main,(60,192,60+mw,192+mh),'탑뷰: 포탑을 깔고 입구를 지킨다')

# feature cards (right)
cards=[
    (s2.crop((1060,520,1817,835)),'Tab 한 번에 1인칭','탑뷰로 지휘하다가 바로 1인칭으로\n직접 조준해서 쏜다'),
    (s3.crop((40,60,770,800)),'웨이브마다 재능 선택','재능을 골라 쌓고, 엘리트 웨이브 뒤엔\n상점에서 무기·포탑을 산다'),
    (None,'직업 8가지','공돌이부터 대통령까지,\n직업마다 다른 장단점'),
]
cx0=1110; cw=W-60-cx0; ch=round((mh-2*18)/3)
# 직업 8칸을 4x2로 다시 배치
tiles=Image.new('RGB',(4*150+10,round((4*150+10)/1.6)),PANEL)
for k in range(8):
    x0=562+k*150; t_=s4.crop((x0,122,x0+136,274))
    tiles.paste(t_,(5+(k%4)*150,(tiles.height-310)//2+(k//4)*155))
cards[2]=(tiles,cards[2][1],cards[2][2])
for i,(im,title,body) in enumerate(cards):
    y=192+i*(ch+18)
    d.rectangle([cx0,y,cx0+cw,y+ch],fill=PANEL,outline=LINE,width=3)
    th=ch-24; tw_=round(th*1.6)
    t=im.copy(); 
    # fit crop to tw_:th by center-cropping
    r=tw_/th
    if t.width/t.height>r: nw=round(t.height*r); t=t.crop(((t.width-nw)//2,0,(t.width-nw)//2+nw,t.height))
    else: nh=round(t.width/r); t=t.crop((0,(t.height-nh)//2,t.width,(t.height-nh)//2+nh))
    framed(t,(cx0+12,y+12,cx0+12+tw_,y+12+th))
    tx=cx0+12+tw_+24
    d.text((tx,y+22),title,font=font(28,True),fill=ACC)
    d.multiline_text((tx,y+68),body,font=font(20),fill=TXT,spacing=10)

# bottom: key guide + how to run
ky=192+mh+34
kg=s4.crop((105,527,1712,843))
kh=H-ky-40; kw=round(kh*kg.width/kg.height)
framed(kg,(60,ky,60+kw,ky+kh))
bx=60+kw+30
d.rectangle([bx,ky,W-60,ky+kh],fill=PANEL,outline=LINE,width=3)
d.text((bx+24,ky+18),'실행 방법',font=font(26,True),fill=ACC)
steps=['1. zip 압축을 푼다','2. Project_D.exe 실행','3. "PC 보호" 창이 뜨면 → 추가 정보 → 실행','최고 생존 기록은 PC에 저장됩니다']
for j,st in enumerate(steps):
    d.text((bx+24,ky+66+j*34),st,font=font(20),fill=SUB if j==3 else TXT)
img.save('Project_D_소개.png',optimize=True)
print(img.size, kw, kh, mh)
