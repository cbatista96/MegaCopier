from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import math, os
r=Path(__file__).resolve().parents[2]; out=r/'installer/Assets'; out.mkdir(exist_ok=True)
# Composición propia con el icono original: resolución triple para pantallas de alta densidad.
w,h=492,942
im=Image.new('RGB',(w,h)); px=im.load()
for y in range(h):
 for x in range(w):
  glow=math.exp(-(((x-430)/360)**2+((y-600)/490)**2))
  violet=math.exp(-(((x-10)/370)**2+((y-900)/240)**2))
  px[x,y]=(int(17+20*glow+31*violet),int(23+30*glow+7*violet),int(40+52*glow+65*violet))
d=ImageDraw.Draw(im)
for n in range(3):
 x=176+n*42; y=510+n*48
 d.rounded_rectangle((x,y,x+247,y+306),radius=25,outline=(66+n*13,82+n*9,132+n*17),width=2)
d.rounded_rectangle((45,61,113,67),radius=3,fill='#64ccf5')
logo=Image.open(r/'src/MegaCopier.App/Assets/MegaCopier.ico').convert('RGBA').resize((168,168),Image.Resampling.LANCZOS)
im.paste(logo,(57,166),logo)
font=ImageFont.truetype(str(Path(os.environ.get('WINDIR', 'C:/Windows'))/'Fonts/seguisb.ttf'),60)
d.text((48,373),'MegaCopier',font=font,fill='#f4f7ff')
d.line((54,860,438,860),fill='#617ba9',width=2)
d.ellipse((54,887,64,897),fill='#74d8ef')
d.line((81,892,143,892),fill='#a3b8dc',width=2)
im.save(out/'Wizard.bmp')
small=Image.new('RGB',(174,174),'white'); logo=logo.resize((138,138),Image.Resampling.LANCZOS);small.paste(logo,(18,18),logo);small.save(out/'WizardSmall.bmp')
print(out)
