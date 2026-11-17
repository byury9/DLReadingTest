"""
프로그램 테스트용 샘플 이미지 생성 (실제 카메라 이미지 스타일).

assets/plate_clean.png (실제 이미지에서 영숫자 인쇄만 지운 바탕) 위에
박스 10개 모두 같은 제조번호/사용기한을 찍는다. 일부 이미지는 "쓸림"을 넣는다.
  - sample_01~04: 정상
  - sample_05~08: 움직임 번짐 (가로 방향으로 글자가 끌림)
  - sample_09~12: 인쇄 문질림 (일부 박스의 획이 지워지거나 번짐)

    python make_samples.py --out ../samples
"""
import argparse
import random
from pathlib import Path

import numpy as np
from PIL import Image

import gen_label as gl


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default="../samples")
    ap.add_argument("--fonts-dir", default=None)
    ap.add_argument("--seed", type=int, default=2026)
    args = ap.parse_args()

    gl.init_worker(args.fonts_dir, args.seed)
    random.seed(args.seed)
    np.random.seed(args.seed)
    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)

    # 실제 인쇄와 비슷한 가는 산세리프 폰트 우선
    prefer = [f for f in gl._G["code_fonts"]
              if any(k in Path(f).name for k in ("DejaVuSans.ttf", "Inter-Light", "LiberationSans-Regular"))]

    answers = []
    for i in range(12):
        lot = "GWTB016P" if i % 3 == 0 else gl.random_lot()[:9]
        date = "{}.{:02d}.{:02d}".format(random.randint(2027, 2032), random.randint(1, 12), random.randint(1, 28))

        style = gl.CodeStyle(h=random.uniform(28, 31), font_path=random.choice(prefer) if prefer else None)
        style.pitch_ratio = random.uniform(1.0, 1.05)
        style.dot_ratio = 1.0
        style.color = random.uniform(225, 250)
        style.alpha = 1.0

        img = gl._G["plate"] * random.uniform(0.95, 1.05)
        text_mask = np.zeros_like(img)
        boxes = []
        rub = 0.5 if i >= 8 else 0.0
        for b in gl._G["plate_boxes"]:
            gl.draw_box_text(text_mask, boxes, b, style, None, texts=[lot, date], rub_p=rub, x_jitter=30)
        res = gl.compose(img, text_mask, style)
        if 4 <= i < 8:
            res = gl.motion_blur(res, style.h * random.uniform(0.25, 0.5), random.uniform(-2, 2))
        res = res + np.random.normal(0, 2.5, res.shape)
        name = f"sample_{i + 1:02d}.png"
        Image.fromarray(np.clip(res, 0, 255).astype(np.uint8)).save(out / name)
        kind = "정상" if i < 4 else ("움직임 번짐" if i < 8 else "인쇄 문질림")
        answers.append(f"{name}\t{lot}\t{date}\t{kind}")
        print(answers[-1])
    (out / "answers.txt").write_text("파일\t제조번호\t사용기한\t종류\n" + "\n".join(answers) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
