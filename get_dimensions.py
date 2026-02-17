
import os
import struct

def get_image_size(file_path):
    with open(file_path, 'rb') as f:
        data = f.read(25)
        if len(data) < 24:
            return None
        if data[:8] == b'\x89PNG\r\n\x1a\n':
            w, h = struct.unpack('>LL', data[16:24])
            return int(w), int(h)
    return None

folder = r'd:\Fish Game\Assets\Graphics\fish'
for filename in os.listdir(folder):
    if filename.endswith('.png'):
        path = os.path.join(folder, filename)
        size = get_image_size(path)
        if size:
            print(f'{filename}: {size[0]}x{size[1]}')
