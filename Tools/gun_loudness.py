"""총소리 크기 재기 (v161): python gun_loudness.py Assets/Resources/Sound/Gun/*_Fire*.wav Assets/Resources/Sound/Gun/Battle_fire.wav
K-가중(BS.1770) 400ms·100ms 최대 음량의 평균 → 콜트 기준 dB 표 (Sfx.FireGainDb에 붙여 넣기)"""
import numpy as np, glob, math, sys
from scipy.io import wavfile
from scipy import signal
def kweight(x, fs):
    f0=1681.974450955533; G=3.999843853973347; Q=0.7071752369554196
    K=math.tan(math.pi*f0/fs); Vh=10**(G/20); Vb=Vh**0.4996667741545416
    a0=1+K/Q+K*K
    b=[(Vh+Vb*K/Q+K*K)/a0, 2*(K*K-Vh)/a0, (Vh-Vb*K/Q+K*K)/a0]; a=[1,2*(K*K-1)/a0,(1-K/Q+K*K)/a0]
    y=signal.lfilter(b,a,x)
    f0=38.13547087602444; Q=0.5003270373238773; K=math.tan(math.pi*f0/fs)
    a=[1,2*(K*K-1)/(1+K/Q+K*K),(1-K/Q+K*K)/(1+K/Q+K*K)]; b=[1,-2,1]
    return signal.lfilter(b,a,y)
def load(f):
    fs,x=wavfile.read(f)
    if x.dtype.kind=='i': x=x.astype(np.float64)/np.iinfo(x.dtype).max
    elif x.dtype.kind=='u': x=(x.astype(np.float64)-128)/128
    if x.ndim>1: x=x.mean(1)
    return fs,x
def mmax(y,fs,w):
    win=int(w*fs); p=np.concatenate([y**2,np.zeros(win)])
    c=np.cumsum(np.concatenate([[0],p]))
    ms=(c[win:]-c[:-win])/win
    return -0.691+10*np.log10(ms.max()+1e-12)
out={}
for f in sys.argv[1:]:
    fs,x=load(f); y=kweight(x,fs)
    a,b=mmax(y,fs,0.4),mmax(y,fs,0.1)
    out[f]=(a+b)/2
    print(f"{f:28s} 400ms {a:6.1f}  100ms {b:6.1f}  avg {(a+b)/2:6.1f}")
ref = next((v for k, v in out.items() if k.endswith('Colt_Fire.wav')), None)
if ref is not None:
    print('\n// Sfx.FireGainDb (콜트 기준 dB)')
    for k, v in out.items():
        print(f'{{ "{k.split("/")[-1][:-4]}", {ref - v:.1f}f }},')
