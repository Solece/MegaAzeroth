"""Decode installed BLP textures, including separate paletted BLP2 alpha.

Pillow's _read_bgra currently reads alpha from palette entries and ignores the
separate pixel alpha plane; this corrupts paletted BLP2 with depth 1/4/8.
"""
import io, math, struct
import numpy as np
from PIL import Image
def decode_blp(b):
 if b[:4]==b'BLP2' and b[8]==1:
  w,h=struct.unpack_from('<II',b,12);off=struct.unpack_from('<I',b,20)[0];count=w*h
  palette=np.frombuffer(b[148:1172],dtype=np.uint8).reshape(256,4)
  ix=np.frombuffer(b[off:off+count],dtype=np.uint8)
  px=palette[ix][:,[2,1,0,3]].copy();depth=b[9];alpha=b[off+count:]
  if depth==0:px[:,3]=255
  elif depth==8:px[:,3]=np.frombuffer(alpha[:count],dtype=np.uint8)
  elif depth in [1,4]:
   bits=np.frombuffer(alpha[:math.ceil(count*depth/8)],dtype=np.uint8)
   shifts=(np.arange(count)*depth)%8
   px[:,3]=((bits[np.arange(count)*depth//8]>>shifts)&((1<<depth)-1))*255//((1<<depth)-1)
  else:raise ValueError('Unsupported paletted BLP2 alpha depth '+str(depth))
  return Image.fromarray(px.reshape(h,w,4))
 return Image.open(io.BytesIO(b)).convert('RGBA')
