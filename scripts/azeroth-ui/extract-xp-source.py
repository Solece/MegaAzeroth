from pathlib import Path
import sys,re,json
root=Path(r'D:/Games/Saitan/AzerothMegabonkLab');sys.path.insert(0,str(root/'scripts/azeroth-assets'));from mpq_source import Archives
arc=Archives(); p='Interface\\FrameXML\\MainMenuBar.lua';b,a=arc.read(p)
out=root/'private-assets/azeroth-ui/verified'/p.replace('\\','/');out.write_bytes(b)
text=b.decode('utf8','replace'); start=text.find('function MainMenuExpBar_Update')
print(text[start:start+4800]); print('source',p,a,len(b))
