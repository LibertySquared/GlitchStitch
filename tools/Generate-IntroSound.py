"""Original GlitchStich ident: distant approach, obstacle somersault, gap corkscrew, impact, projector lock.
Timings follow SplashWindow.SceneTime; no sampled or third-party recordings.
"""
from pathlib import Path
import wave
import numpy as np
RATE = 48000
DURATION = 6.2
t = np.arange(round(RATE * DURATION)) / RATE
rng = np.random.default_rng(813)
noise = rng.normal(0, 1, len(t))
low = np.convolve(noise, np.ones(31) / 31, mode="same")
high = noise - np.convolve(noise, np.ones(7) / 7, mode="same")
left = np.zeros_like(t)
right = np.zeros_like(t)

def event(start, length, signal, gain=1, pan=0):
    u = np.clip((t-start)/length, 0, 1)
    env = np.sin(np.pi*u)**2 * ((t >= start) & (t < start+length))
    left[:] += signal * env * gain * np.sqrt((1-pan)/2)
    right[:] += signal * env * gain * np.sqrt((1+pan)/2)

# Servo footfalls accelerate left to center; short restrained metallic transients.
for i, start in enumerate((.08, .25, .42, .62, 1.76, 1.91)):
    u = np.maximum(0,t-start)
    hit = (.5*np.sin(2*np.pi*110*u)*np.exp(-u*35) + .24*high*np.exp(-u*95))
    event(start,.12,hit,.65,-.8+i*.2)
# Takeoff and two broad stereo whip arcs synchronized to the mesh rotations.
for start, length, pan in ((.78,.88,-.45),(1.98,1.22,.35)):
    u = np.maximum(0,t-start)
    sweep = np.sin(2*np.pi*(150*u+240*u*u))*.07 + low*.9 + high*.075
    event(start,length,sweep,.75,pan)
    event(start+.11,length,sweep,.25,-pan)
# Mechanical reel ticks while airborne.
for i, start in enumerate(np.arange(2.08,3.17,.16)):
    u = np.maximum(0,t-start)
    event(float(start),.045,np.sin(2*np.pi*1380*u)*np.exp(-u*80),.12,(-1)**i*.4)
# A small vault touchdown, then the main landing at 3.25 s: sub hit and granular metal, short reverb tail.
u=np.maximum(0,t-1.70)
event(1.68,.15,np.sin(2*np.pi*105*u)*np.exp(-u*24)+low*.13,.36,-.2)
u=np.maximum(0,t-3.25)
impact=(np.sin(2*np.pi*(68*u-7*u*u))*.6*np.exp(-u*8)+high*.35*np.exp(-u*35)+low*.7*np.exp(-u*15))
event(3.24,.52,impact,1.3)
event(3.35,.55,impact,.22,.3)
# Lens servo rotation, optical power-up, then a three-note signature at lock.
u=np.maximum(0,t-3.78)
event(3.78,.43,np.sin(2*np.pi*(260*u+650*u*u))*.16+low*.19,.6,.15)
u=np.maximum(0,t-4.08)
event(4.08,.43,np.sin(2*np.pi*(380*u+1400*u*u))*.18+high*.06,.65)
for freq,start in ((329.63,4.36),(493.88,4.47),(659.25,4.60)):
    u=np.maximum(0,t-start)
    tone=(np.sin(2*np.pi*freq*u)+.23*np.sin(2*np.pi*freq*2*u))*np.exp(-u*2.7)
    event(start,1.35,tone,.21)
# Low quiet projector bed under the finished wall title; fade before editor reveal.
u=np.maximum(0,t-4.3)
event(4.3,1.65,np.sin(2*np.pi*55*u)*.04+low*.065,.4)
for channel in (left,right):
    dry=channel.copy()
    for delay,gain in ((.085,.11),(.18,.055)):
        offset=round(delay*RATE); channel[offset:]+=gain*dry[:-offset]
stereo=np.stack((left,right),axis=1)
fade=np.minimum(1,t/.012)*np.minimum(1,np.maximum(0,DURATION-t)/.28)
stereo*=fade[:,None]
stereo*=.69/max(float(np.max(np.abs(stereo))),1e-6)
output=Path(__file__).resolve().parents[1]/"Resources"/"Audio"/"Intro.wav"
with wave.open(str(output),'wb') as wav:
    wav.setnchannels(2);wav.setsampwidth(2);wav.setframerate(RATE);wav.writeframes(np.round(stereo*32767).astype('<i2').tobytes())
print(f"Created {output}: {DURATION}s stereo, peak {np.max(np.abs(stereo)):.3f}")


