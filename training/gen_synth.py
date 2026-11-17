"""
알파벳/숫자 문자 검출용 YOLO 합성 데이터셋 생성기.

각 문자(0-9, A-Z)를 하나의 클래스로 보고, 문자마다 바운딩 박스를 라벨링한다.
- 다양한 폰트 / 크기 / 자간 / 회전 / 기울임
- 도트 매트릭스(잉크젯 인쇄 Lot 번호 스타일) 렌더링
- 배경(단색, 그라데이션, 노이즈, 브러시드 메탈, 박스 골판지 톤), 방해 도형
- 블러 / 노이즈 / 조명 불균일 / JPEG 압축 / 반전

사용 예:
    python gen_synth.py --out datasets/alnum --train 6000 --val 600
    python gen_synth.py --out datasets/alnum --fonts-dir C:\\Windows\\Fonts
"""
import argparse
import math
import os
import random
import re
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

DEFAULT_CHARSET = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ"

# 라틴 문자를 제대로 가진 폰트만 고르기 위한 파일명 패턴
FONT_ALLOW = re.compile(
    r"(DejaVu|Liberation|FreeSans|FreeMono|FreeSerif|Inter|JetBrainsMono|Carlito|Caladea|"
    r"Courier|Charter|NotoSans-|NotoSerif-|NotoSansMono|NotoSansDisplay|NotoMono|"
    r"arial|times|cour|verdana|tahoma|consola|calibri|segoe|georgia|trebuc|lucon|impact|"
    r"bahnschrift|ocr|din)",
    re.IGNORECASE,
)
FONT_DENY = re.compile(r"(Emoji|Symbol|Math|Ornament|Dingbat|wingding|webding|marlett)", re.IGNORECASE)


def find_fonts(fonts_dir):
    dirs = [fonts_dir] if fonts_dir else []
    if not dirs:
        if os.name == "nt":
            dirs = [os.path.join(os.environ.get("WINDIR", r"C:\Windows"), "Fonts")]
        else:
            dirs = ["/usr/share/fonts", "/usr/local/share/fonts", os.path.expanduser("~/.fonts")]
    fonts = []
    for d in dirs:
        for p in Path(d).rglob("*"):
            if p.suffix.lower() not in (".ttf", ".otf"):
                continue
            if not FONT_ALLOW.search(p.name) or FONT_DENY.search(p.name):
                continue
            try:
                f = ImageFont.truetype(str(p), 32)
                if f.getbbox("A8")[2] <= 0:
                    continue
            except Exception:
                continue
            fonts.append(str(p))
    if not fonts:
        raise SystemExit("사용할 폰트를 찾지 못했습니다. --fonts-dir 로 폰트 폴더를 지정하세요.")
    return sorted(set(fonts))


# ---------------------------------------------------------------- 배경
def rand_color(lo=0, hi=255):
    return tuple(int(random.uniform(lo, hi)) for _ in range(3))


def smooth_noise(w, h, scale):
    sw, sh = max(2, w // scale), max(2, h // scale)
    small = np.random.rand(sh, sw).astype(np.float32)
    img = Image.fromarray((small * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC)
    return np.asarray(img, dtype=np.float32) / 255.0


def make_background(w, h):
    kind = random.choices(
        ["solid", "gradient", "noise", "brushed", "carton", "paper"], weights=[2, 2, 2, 1.5, 1.5, 2]
    )[0]
    if kind == "solid":
        arr = np.ones((h, w, 3), np.float32) * np.array(rand_color(), np.float32)
    elif kind == "gradient":
        c1, c2 = np.array(rand_color(), np.float32), np.array(rand_color(), np.float32)
        ang = random.uniform(0, math.pi)
        yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
        t = xx * math.cos(ang) + yy * math.sin(ang)
        t = (t - t.min()) / max(1e-6, t.max() - t.min())
        arr = c1 * (1 - t[..., None]) + c2 * t[..., None]
    elif kind == "noise":
        base = np.array(rand_color(), np.float32)
        n = smooth_noise(w, h, random.choice([4, 8, 16, 32]))
        arr = base * (0.6 + 0.4 * n[..., None])
    elif kind == "brushed":
        g = random.uniform(90, 220)
        n = np.random.rand(h, w).astype(np.float32)
        img = Image.fromarray((n * 255).astype(np.uint8)).filter(ImageFilter.BoxBlur(1))
        img = img.resize((max(2, w // 20), h)).resize((w, h), Image.BILINEAR)
        n = np.asarray(img, np.float32) / 255.0
        arr = np.repeat((g * (0.75 + 0.5 * (n - 0.5)))[..., None], 3, axis=2)
        arr = arr * np.array([random.uniform(0.9, 1.1) for _ in range(3)], np.float32)
    elif kind == "carton":
        base = np.array([random.uniform(150, 210), random.uniform(110, 160), random.uniform(60, 110)], np.float32)
        n = smooth_noise(w, h, random.choice([2, 4, 8]))
        arr = base * (0.8 + 0.3 * n[..., None])
    else:  # paper
        g = random.uniform(200, 250)
        n = smooth_noise(w, h, 2)
        arr = np.ones((h, w, 3), np.float32) * g * (0.92 + 0.08 * n[..., None])
    return np.clip(arr, 0, 255)


def draw_distractors(img):
    d = ImageDraw.Draw(img)
    w, h = img.size
    for _ in range(random.randint(0, 4)):
        c = rand_color()
        k = random.random()
        if k < 0.4:  # 긴 선 (문자처럼 보이지 않게 길게)
            if random.random() < 0.5:
                y = random.randint(0, h)
                d.line([(0, y), (w, y + random.randint(-10, 10))], fill=c, width=random.randint(1, 3))
            else:
                x = random.randint(0, w)
                d.line([(x, 0), (x + random.randint(-10, 10), h)], fill=c, width=random.randint(1, 2))
        elif k < 0.7:  # 사각 테두리
            x0, y0 = random.randint(0, w // 2), random.randint(0, h // 2)
            d.rectangle([x0, y0, x0 + random.randint(w // 4, w), y0 + random.randint(h // 4, h)],
                        outline=c, width=random.randint(1, 3))
        else:  # 큰 원
            r = random.randint(min(w, h) // 4, min(w, h))
            x0, y0 = random.randint(-r, w), random.randint(-r, h)
            d.ellipse([x0, y0, x0 + r, y0 + r], outline=c, width=random.randint(1, 3))


# ---------------------------------------------------------------- 텍스트
def random_text(charset):
    n = random.choices([random.randint(1, 4), random.randint(4, 10), random.randint(10, 16)], weights=[1, 4, 2])[0]
    style = random.random()
    if style < 0.35:  # Lot 번호 / 날짜 스타일
        groups = []
        while sum(len(g) for g in groups) < n:
            pool = random.choice([DEFAULT_CHARSET[:10], DEFAULT_CHARSET[10:], charset])
            pool = "".join(c for c in pool if c in charset) or charset
            groups.append("".join(random.choice(pool) for _ in range(random.randint(2, 6))))
        return " ".join(groups)
    return "".join(random.choice(charset) for _ in range(n))


def dot_matrix(mask, char_h):
    """일반 글자 마스크를 잉크젯 도트 매트릭스 스타일로 변환."""
    # 글자 높이가 도트 7~9개가 되도록 격자 간격을 잡고, 격자 한 칸의 잉크 비율이
    # 임계값 이상이면 그 칸 중심에 도트 하나를 찍는다 (가는/굵은 폰트 모두 안정적)
    pitch = max(2.0, char_h / random.uniform(7.0, 9.5))
    r = pitch * random.uniform(0.33, 0.48)
    thr = random.uniform(0.25, 0.4)
    w, h = mask.size
    ox, oy = random.uniform(0, pitch), random.uniform(0, pitch)
    gw, gh = int((w - ox) / pitch), int((h - oy) / pitch)
    if gw < 1 or gh < 1:
        return mask
    # 가는 폰트(획이 도트 간격보다 훨씬 얇음)는 도트가 사라지므로 획을 먼저 두껍게
    arr = np.asarray(mask)
    ys, xs = np.nonzero(arr > 127)
    if len(xs):
        box = arr[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
        if (box > 127).mean() < 0.14:
            k = int(pitch * 0.6) | 1
            if k >= 3:
                mask = mask.filter(ImageFilter.MaxFilter(k))
    region = mask.crop((ox, oy, ox + gw * pitch, oy + gh * pitch))
    cov = np.asarray(region.resize((gw, gh), Image.BOX), np.float32) / 255.0
    out = Image.new("L", (w, h), 0)
    d = ImageDraw.Draw(out)
    for gy, gx in zip(*np.nonzero(cov > thr)):
        x = ox + (gx + 0.5) * pitch + random.gauss(0, pitch * 0.06)
        y = oy + (gy + 0.5) * pitch + random.gauss(0, pitch * 0.06)
        d.ellipse([x - r, y - r, x + r, y + r], fill=255)
    return out


def render_block(w, h, fonts, charset):
    """캔버스 크기의 마스크(L)와 문자 박스 리스트[(cls, x0, y0, x1, y1)] 반환."""
    mask = Image.new("L", (w, h), 0)
    boxes = []
    n_lines = random.choices([1, 2, 3, 4], weights=[5, 3, 1.5, 0.7])[0]
    char_h = random.uniform(0.06, 0.8 / n_lines) * h
    char_h = max(10.0, char_h)
    font_path = random.choice(fonts)
    size = int(char_h * 1.35)
    try:
        font = ImageFont.truetype(font_path, size)
    except Exception:
        return mask, boxes
    dot = random.random() < 0.3
    bold = random.random() < 0.2
    spacing = random.uniform(-0.02, 0.35) * size
    line_gap = random.uniform(1.15, 1.8) * char_h
    total_h = line_gap * (n_lines - 1) + char_h
    y = random.uniform(0, max(1.0, h - total_h)) - char_h * 0.25
    align = random.choice(["left", "center", "right"])
    lines = [random_text(charset) for _ in range(n_lines)]
    widths = [sum(font.getlength(c) + spacing for c in t) for t in lines]
    block_w = max(widths)
    if block_w > w * 0.98:  # 너무 길면 줄 끝을 잘라냄
        lines = [t[: max(1, int(len(t) * w * 0.95 / block_w))] for t in lines]
        widths = [sum(font.getlength(c) + spacing for c in t) for t in lines]
        block_w = max(widths)
    bx = random.uniform(0, max(1.0, w - block_w))

    draw = ImageDraw.Draw(mask)
    for t, lw in zip(lines, widths):
        if align == "left":
            x = bx
        elif align == "center":
            x = bx + (block_w - lw) / 2
        else:
            x = bx + block_w - lw
        for ch in t:
            if ch != " ":
                bb = draw.textbbox((x, y), ch, font=font, stroke_width=1 if bold else 0)
                draw.text((x, y), ch, font=font, fill=255, stroke_width=1 if bold else 0, stroke_fill=255)
                if bb[2] - bb[0] >= 2 and bb[3] - bb[1] >= 4:
                    boxes.append([charset.index(ch), bb[0], bb[1], bb[2], bb[3]])
            x += font.getlength(ch) + spacing
        y += line_gap

    if dot:
        mask = dot_matrix(mask, char_h)
        pad = char_h / 14
        boxes = [[c, x0 - pad, y0 - pad, x1 + pad, y1 + pad] for c, x0, y0, x1, y1 in boxes]
    return mask, boxes


def affine_jitter(mask, boxes):
    """회전/기울임/스케일을 마스크와 박스에 함께 적용."""
    w, h = mask.size
    ang = math.radians(random.gauss(0, 3.0) if random.random() < 0.8 else random.uniform(-10, 10))
    shear = random.uniform(-0.25, 0.25) if random.random() < 0.3 else 0.0
    sc = random.uniform(0.9, 1.05)
    cx, cy = w / 2, h / 2
    ca, sa = math.cos(ang), math.sin(ang)
    # forward: p' = A (p - c) + c,  A = R * S * Shear
    a = np.array([[ca, -sa], [sa, ca]]) @ np.array([[sc, sc * shear], [0, sc]])
    inv = np.linalg.inv(a)
    # PIL transform 은 출력->입력 매핑을 요구
    off = np.array([cx, cy]) - inv @ np.array([cx, cy])
    coeffs = (inv[0, 0], inv[0, 1], off[0], inv[1, 0], inv[1, 1], off[1])
    out = mask.transform((w, h), Image.AFFINE, coeffs, resample=Image.BILINEAR)
    nb = []
    for c, x0, y0, x1, y1 in boxes:
        pts = np.array([[x0, y0], [x1, y0], [x1, y1], [x0, y1]], np.float64) - [cx, cy]
        pts = (a @ pts.T).T + [cx, cy]
        nx0, ny0 = pts.min(0)
        nx1, ny1 = pts.max(0)
        # 회전으로 커진 박스를 약간 줄여 글자에 맞춤
        shrink = abs(math.sin(ang)) * 0.15
        dw, dh = (nx1 - nx0) * shrink, (ny1 - ny0) * shrink
        nb.append([c, nx0 + dw / 2, ny0 + dh / 2, nx1 - dw / 2, ny1 - dh / 2])
    return out, nb


def make_sample(fonts, charset):
    w = random.choice([random.randint(256, 640), random.randint(400, 800)])
    h = random.choice([random.randint(96, 256), random.randint(200, 640)])
    bg = make_background(w, h)
    mask, boxes = render_block(w, h, fonts, charset)
    if not boxes:
        return None, None
    if random.random() < 0.85:
        mask, boxes = affine_jitter(mask, boxes)

    # 박스가 캔버스 밖으로 많이 나간 글자는 버림
    kept = []
    for c, x0, y0, x1, y1 in boxes:
        cx0, cy0, cx1, cy1 = max(0, x0), max(0, y0), min(w, x1), min(h, y1)
        if cx1 <= cx0 or cy1 <= cy0:
            continue
        if (cx1 - cx0) * (cy1 - cy0) < 0.6 * (x1 - x0) * (y1 - y0):
            return None, None  # 잘린 글자가 있으면 라벨이 애매해지므로 샘플 폐기
        kept.append([c, cx0, cy0, cx1, cy1])
    if not kept:
        return None, None

    # 글자 색: 배경과 충분한 대비
    bg_lum = bg.mean()
    if random.random() < 0.15:
        color = np.array(rand_color(), np.float32)
    elif bg_lum > 110:
        color = np.array(rand_color(0, 90), np.float32)
    else:
        color = np.array(rand_color(170, 255), np.float32)
    a = np.asarray(mask, np.float32)[..., None] / 255.0
    a = a * random.uniform(0.75, 1.0)
    if random.random() < 0.3:  # 인쇄 얼룩/번짐
        a = a * (0.65 + 0.35 * smooth_noise(w, h, random.choice([2, 4]))[..., None])
    arr = bg * (1 - a) + color * a

    # 조명 불균일
    if random.random() < 0.4:
        yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
        cxl, cyl = random.uniform(0, w), random.uniform(0, h)
        dist = np.sqrt((xx - cxl) ** 2 + (yy - cyl) ** 2) / max(w, h)
        arr = arr * (1.0 + random.uniform(0.1, 0.5) * (0.5 - dist))[..., None]

    img = Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8))
    if random.random() < 0.4:
        draw_distractors(img)
    if random.random() < 0.4:
        img = img.filter(ImageFilter.GaussianBlur(random.uniform(0.3, 1.6)))
    arr = np.asarray(img, np.float32)
    if random.random() < 0.5:
        arr = arr + np.random.normal(0, random.uniform(2, 14), arr.shape)
    if random.random() < 0.3:
        arr = np.repeat(arr.mean(axis=2, keepdims=True), 3, axis=2)
    if random.random() < 0.08:
        arr = 255 - arr
    img = Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8))
    return img, kept


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default="datasets/alnum")
    ap.add_argument("--train", type=int, default=6000)
    ap.add_argument("--val", type=int, default=600)
    ap.add_argument("--charset", default=DEFAULT_CHARSET)
    ap.add_argument("--fonts-dir", default=None)
    ap.add_argument("--seed", type=int, default=0)
    args = ap.parse_args()

    random.seed(args.seed)
    np.random.seed(args.seed)
    fonts = find_fonts(args.fonts_dir)
    print(f"폰트 {len(fonts)}개 사용")
    charset = args.charset
    out = Path(args.out).resolve()

    for split, count in (("train", args.train), ("val", args.val)):
        (out / "images" / split).mkdir(parents=True, exist_ok=True)
        (out / "labels" / split).mkdir(parents=True, exist_ok=True)
        i = 0
        while i < count:
            img, boxes = make_sample(fonts, charset)
            if img is None:
                continue
            w, h = img.size
            name = f"{split}_{i:06d}"
            img.save(out / "images" / split / f"{name}.jpg", quality=random.randint(55, 95))
            with open(out / "labels" / split / f"{name}.txt", "w") as f:
                for c, x0, y0, x1, y1 in boxes:
                    f.write(f"{c} {(x0 + x1) / 2 / w:.6f} {(y0 + y1) / 2 / h:.6f} "
                            f"{(x1 - x0) / w:.6f} {(y1 - y0) / h:.6f}\n")
            i += 1
            if i % 500 == 0:
                print(f"{split}: {i}/{count}")

    with open(out / "data.yaml", "w", encoding="utf-8") as f:
        f.write(f"path: {out.as_posix()}\ntrain: images/train\nval: images/val\nnames:\n")
        for k, c in enumerate(charset):
            f.write(f"  {k}: '{c}'\n")
    print(f"완료: {out / 'data.yaml'}")


if __name__ == "__main__":
    main()
