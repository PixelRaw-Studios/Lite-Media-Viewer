# Put your 100x100 PNG here as logo.png, then run:  python make_icon.py   (needs: pip install pillow)
from PIL import Image
im = Image.open('logo.png').convert('RGBA')
side = max(im.size)
sq = Image.new('RGBA', (side, side), (0, 0, 0, 0))
sq.paste(im, ((side - im.width) // 2, (side - im.height) // 2))
big = sq.resize((256, 256), Image.LANCZOS)
big.save('icon.ico', sizes=[(256,256),(128,128),(64,64),(48,48),(32,32),(16,16)])
print('icon.ico created')
