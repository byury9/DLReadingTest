"""
프로그램 테스트용 샘플 이미지 생성.

생산 라인 카메라 이미지처럼, 큰 이미지의 거의 같은 위치에 인쇄 문자(Lot/날짜 등)가 있고
그 바깥에도 다른 글자(방해 문자)가 있는 이미지를 만든다. 같은 ROI 하나로 모든 이미지를 읽어 볼 수 있다.

    python make_samples.py --out ../samples --count 12
"""
import argparse
import random
import re
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

import gen_synth as gs


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default="../samples")
    ap.add_argument("--count", type=int, default=12)
    ap.add_argument("--fonts-dir", default=None)
    ap.add_argument("--seed", type=int, default=2026)
    args = ap.parse_args()

    random.seed(args.seed)
    np.random.seed(args.seed)
    fonts = gs.find_fonts(args.fonts_dir)
    mono_fonts = [f for f in fonts if "mono" in f.lower() and "bold" in f.lower() and "extrabold" not in f.lower()] or fonts
    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)

    W, H = 1280, 960
    roi_x, roi_y, roi_w, roi_h = 380, 380, 600, 200  # 인쇄 영역 (이미지마다 약간 흔들림)
    base_bg = np.ones((H, W, 3), np.float32) * np.array([196, 170, 128], np.float32)  # 골판지 톤
    base_bg = base_bg * (0.9 + 0.15 * gs.smooth_noise(W, H, 4)[..., None])

    answers = []
    for i in range(args.count):
        bg = base_bg * random.uniform(0.85, 1.1)
        img = Image.fromarray(np.clip(bg, 0, 255).astype(np.uint8))
        d = ImageDraw.Draw(img)

        # 라벨/제품 영역 느낌의 큰 사각형과 방해 글자 (ROI 밖)
        d.rectangle([80, 80, W - 80, H - 80], outline=(60, 60, 60), width=4)
        f_big = ImageFont.truetype(random.choice(fonts), 64)
        d.text((120, 120), "PRODUCT " + "".join(random.choice("ABCDEFGHJKLMNPRSTUVWXYZ") for _ in range(3)),
               font=f_big, fill=(30, 30, 120))
        f_small = ImageFont.truetype(random.choice(fonts), 34)
        d.text((120, 800), "NET 500G  MADE IN KOREA", font=f_small, fill=(40, 40, 40))
        d.text((900, 140), str(random.randint(1000, 9999)), font=f_small, fill=(120, 30, 30))

        # 인쇄 문자 (ROI 안): Lot / 유통기한 형식, 도트 또는 일반 폰트
        lot = "L" + "".join(random.choice("0123456789") for _ in range(4)) + random.choice("ABCDEFGH") + \
              "".join(random.choice("0123456789") for _ in range(2))
        exp = "EXP 20{:02d} {:02d} {:02d}".format(random.randint(26, 29), random.randint(1, 12), random.randint(1, 28))
        size = 54
        font = ImageFont.truetype(random.choice(mono_fonts), size)
        layer = Image.new("L", (roi_w, roi_h), 0)
        ld = ImageDraw.Draw(layer)
        ld.text((20, 20), lot, font=font, fill=255)
        ld.text((20, 105), exp, font=font, fill=255)
        if i % 2 == 0:
            layer = gs.dot_matrix(layer, size * 0.75)
        layer = layer.rotate(random.uniform(-2, 2), resample=Image.BILINEAR)
        ox, oy = roi_x + random.randint(-15, 15), roi_y + random.randint(-10, 10)
        ink = Image.new("RGB", layer.size, (15, 15, 15))
        img.paste(ink, (ox, oy), layer)

        if random.random() < 0.5:
            img = img.filter(ImageFilter.GaussianBlur(random.uniform(0.3, 1.0)))
        arr = np.asarray(img, np.float32) + np.random.normal(0, 4, (H, W, 3))
        img = Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8))
        name = out / f"sample_{i + 1:02d}.jpg"
        img.save(name, quality=90)
        answers.append(f"{name.name}\t{lot}\t{exp}")
        print(name, lot, exp)
    (out / "answers.txt").write_text("\n".join(answers) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
