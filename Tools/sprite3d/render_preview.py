import numpy as np, json, sys
from PIL import Image
d=json.load(open('Rifle_M4_mesh.json')); tex=np.asarray(Image.open('Rifle_M4.png').convert('RGBA'))
TH,TW=tex.shape[:2]
def rot(yaw,pitch):
    cy,sy=np.cos(np.radians(yaw)),np.sin(np.radians(yaw)); cp,sp=np.cos(np.radians(pitch)),np.sin(np.radians(pitch))
    Ry=np.array([[cy,0,sy],[0,1,0],[-sy,0,cy]]); Rx=np.array([[1,0,0],[0,cp,-sp],[0,sp,cp]]); return Rx@Ry
def render(yaw,pitch,Wd=520,Hd=260,fn='r.png'):
    R=rot(yaw,pitch); img=np.full((Hd,Wd,3),235,np.uint8); zb=np.full((Hd,Wd),1e9)
    allv=np.concatenate([np.array(g['v']).reshape(-1,3) for g in d['groups']]); c=allv.mean(0)
    sc=Wd*0.9/ (allv.max(0)-allv.min(0)).max()
    for g in d['groups']:
        V=(np.array(g['v']).reshape(-1,3)-c)@R.T; UV=np.array(g['uv']).reshape(-1,2); T=np.array(g['t']).reshape(-1,3)
        P=np.stack([V[:,0]*sc+Wd/2, -V[:,1]*sc+Hd/2, V[:,2]],1)
        for t in T:
            a,b,cc=P[t]
            # backface: viewer looks toward +z (camera at -z); keep tris whose normal points to -z
            n=np.cross(V[t[1]]-V[t[0]],V[t[2]]-V[t[0]])
            if n[2]>=0: continue
            xs=[a[0],b[0],cc[0]]; ys=[a[1],b[1],cc[1]]
            x0,x1=int(max(0,np.floor(min(xs)))),int(min(Wd-1,np.ceil(max(xs)))); y0,y1=int(max(0,np.floor(min(ys)))),int(min(Hd-1,np.ceil(max(ys))))
            if x1<x0 or y1<y0: continue
            X,Y=np.meshgrid(np.arange(x0,x1+1)+0.5,np.arange(y0,y1+1)+0.5)
            den=(b[1]-cc[1])*(a[0]-cc[0])+(cc[0]-b[0])*(a[1]-cc[1])
            if abs(den)<1e-9: continue
            w0=((b[1]-cc[1])*(X-cc[0])+(cc[0]-b[0])*(Y-cc[1]))/den; w1=((cc[1]-a[1])*(X-cc[0])+(a[0]-cc[0])*(Y-cc[1]))/den; w2=1-w0-w1
            m=(w0>=-1e-6)&(w1>=-1e-6)&(w2>=-1e-6)
            z=w0*a[2]+w1*b[2]+w2*cc[2]
            yy,xx=np.where(m); 
            for i,j in zip(yy,xx):
                Y_,X_=y0+i,x0+j
                if z[i,j]<zb[Y_,X_]:
                    zb[Y_,X_]=z[i,j]; uv=w0[i,j]*UV[t[0]]+w1[i,j]*UV[t[1]]+w2[i,j]*UV[t[2]]
                    tx=min(TW-1,max(0,int(uv[0]*TW))); ty=min(TH-1,max(0,int((1-uv[1])*TH)))
                    img[Y_,X_]=tex[ty,tx,:3]
    Image.fromarray(img).save(fn)
render(float(sys.argv[1]),float(sys.argv[2]),fn=sys.argv[3])
