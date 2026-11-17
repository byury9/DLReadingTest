"""
제품 박스 인쇄(제조번호 / 사용기한) 문자 검출용 YOLO 학습 데이터 생성기.

실제 카메라 이미지(assets/plate_clean.png: 원본에서 영숫자 인쇄만 지운 이미지)와
합성 박스 이미지 위에, 실제와 비슷한 흰색 단선(모노라인) 글자를 넓은 자간으로 찍고
글자마다(0-9, A-Z) 바운딩 박스를 라벨링한다. '.' 은 라벨링하지 않는다.

이미지 "쓸림" 재현:
- 움직임 번짐(motion blur): 컨베이어 이동 방향(주로 가로)으로 글자가 끌려 보임
- 인쇄 문질림(rub): 획 일부가 지워지거나 한쪽으로 번진 꼬리가 생김

구성 비율(기본): 실제 바탕 55% / 합성 박스 30% / 범용(gen_synth) 15%

사용 예:
    python gen_label.py --out datasets/label --train 4000 --val 400
"""
import argparse
import json
import math
import random
from multiprocessing import Pool
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

import gen_synth as gs

CHARSET = gs.DEFAULT_CHARSET
HERE = Path(__file__).resolve().parent
ASSETS = HERE / "assets"

KOREAN_LABELS = [("제조번호 :", "사용기한 :"), ("제조번호 :", "유통기한 :"), ("제조번호", "사용기한"),
                 ("제조일자 :", "사용기한 :"), ("품목번호 :", "유통기한 :")]

_G = {}  # 워커별 캐시 (폰트 목록, 바탕 이미지)


def init_worker(fonts_dir, seed):
    import os
    random.seed(seed + os.getpid())
    np.random.seed((seed + os.getpid()) % (2 ** 31))
    fonts = gs.find_fonts(fonts_dir)
    _G["fonts"] = fonts
    code = [f for f in fonts if any(k in Path(f).name.lower() for k in
                                    ("sans", "inter", "mono", "carlito", "arial", "verdana", "tahoma",
                                     "consola", "segoe", "calibri", "bahnschrift", "din", "ocr"))]
    # 실제 인쇄는 가는 단선(모노라인) 산세리프: 굵은/기울임/세리프 계열 제외
    code = [f for f in code if not any(k in Path(f).name.lower() for k in (
        "italic", "oblique", "condensed", "bold", "black", "heavy", "semibold", "medium",
        "freemono", "courier", "serif", "display"))]
    _G["code_fonts"] = code or fonts
    _G["ko_fonts"] = find_korean_fonts(fonts_dir)
    plate = Image.open(ASSETS / "plate_clean.png").convert("L")
    _G["plate"] = np.asarray(plate, np.float32)
    meta = json.load(open(ASSETS / "plate_slots.json", encoding="utf-8"))
    slots = sorted(meta["slots"], key=lambda s: (s["col"], s["top"]))
    boxes = []
    for i in range(0, len(slots) - 1, 2):
        lot, date = slots[i], slots[i + 1]
        x0, x1 = (95, 640) if lot["col"] == 0 else (648, 1212)
        boxes.append({"rect": [x0, lot["top"] - 38, x1, date["top"] + date["h"] + 38],
                      "x": lot["x"], "tops": [lot["top"], date["top"]], "h": lot["h"],
                      "pitch": lot["pitch"], "label_x": x0 + 40})
    _G["plate_boxes"] = boxes


def find_korean_fonts(fonts_dir):
    import os
    cands = []
    dirs = [fonts_dir] if fonts_dir else (
        [os.path.join(os.environ.get("WINDIR", r"C:\Windows"), "Fonts")] if os.name == "nt"
        else ["/usr/share/fonts", "/usr/local/share/fonts"])
    for d in dirs:
        for p in Path(d).rglob("*"):
            n = p.name.lower()
            if p.suffix.lower() in (".ttf", ".ttc", ".otf") and any(
                    k in n for k in ("malgun", "gulim", "batang", "dotum", "nanum", "wqy", "notosanscjk",
                                     "notosanskr", "notoserifcjk", "unfonts", "baekmuk")):
                try:
                    f = ImageFont.truetype(str(p), 20)
                    if f.getbbox("한")[2] > 0:
                        cands.append(str(p))
                except Exception:
                    pass
    return cands


# ---------------------------------------------------------------- 텍스트 내용
# 날짜(숫자 8자)가 박스마다 들어가서 숫자가 영문보다 훨씬 많이 학습된다.
# 제조번호는 영문 위주로 만들고, 서로 헷갈리기 쉬운 글자(O/Q/C/G, I/J, M/W/V/N, P/B/R/F 등)는 더 자주 넣는다.
LETTERS = CHARSET[10:]
HARD_LETTERS = "IOQGCMWJPBFUYVKDRN"
LETTER_W = [3.0 if c in HARD_LETTERS else 1.0 for c in LETTERS]


def rand_letters(n):
    return "".join(random.choices(LETTERS, weights=LETTER_W, k=n))


def rand_digits(n):
    return "".join(random.choice(CHARSET[:10]) for _ in range(n))


def random_lot():
    k = random.random()
    if k < 0.40:  # GWTB016P 형식: 영문 3~5 + 숫자 2~4 + 영문 0~2
        return rand_letters(random.randint(3, 5)) + rand_digits(random.randint(2, 4)) + rand_letters(random.randint(0, 2))
    if k < 0.75:  # 영문만
        return rand_letters(random.randint(5, 10))
    if k < 0.95:  # 영문/숫자 섞임 (영문 70%)
        return "".join(rand_letters(1) if random.random() < 0.7 else rand_digits(1)
                       for _ in range(random.randint(5, 10)))
    return rand_digits(random.randint(5, 10))


def random_date():
    y, m, d = random.randint(2020, 2039), random.randint(1, 12), random.randint(1, 31)
    k = random.random()
    if k < 0.6:
        return f"{y}.{m:02d}.{d:02d}"
    if k < 0.75:
        return f"{y}{m:02d}{d:02d}"
    if k < 0.9:
        return f"{y % 100:02d}.{m:02d}.{d:02d}"
    return f"{y}.{m:02d}"


# ---------------------------------------------------------------- 글자 렌더링
class CodeStyle:
    """한 이미지(같은 프린터) 안에서 공통으로 쓰는 인쇄 스타일."""

    def __init__(self, h, font_path=None):
        self.font_path = font_path or random.choice(_G["code_fonts"])
        self.h = h
        f100 = ImageFont.truetype(self.font_path, 100)
        cap = f100.getbbox("H")
        cap_h = max(1, cap[3] - cap[1])
        self.size = max(6, int(round(h * 100 / cap_h)))
        self.font = ImageFont.truetype(self.font_path, self.size)
        self.cap_top = self.font.getbbox("H")[1]
        # 단선 굵기: 글자 높이의 8~14% 가 되도록 얇은 폰트는 팽창
        stroke_now = self.estimate_stroke()
        target = h * random.uniform(0.08, 0.14)
        k = int(round(max(0.0, target - stroke_now))) | 1
        self.k = k if k >= 3 else 1
        self.pitch_ratio = random.uniform(0.88, 1.15)
        self.dot_ratio = random.uniform(0.5, 1.0)
        self.color = random.uniform(195, 255)
        self.alpha = random.uniform(0.75, 1.0)

    def estimate_stroke(self):
        img = Image.new("L", (self.size * 2, self.size * 2), 0)
        ImageDraw.Draw(img).text((self.size // 2, self.size // 2), "I", font=self.font, fill=255)
        a = np.asarray(img) > 127
        cols = a.any(axis=0).sum()
        return float(cols)


def render_line(draw, text, x, top, style, pitch, boxes):
    """draw(L 마스크)에 한 줄을 그리고 문자 박스를 boxes 에 추가. 끝 x 반환."""
    f = style.font
    pad = style.k // 2
    for ch in text:
        if ch == ".":
            r = max(1.0, style.h * random.uniform(0.045, 0.07))
            cx = x + pitch * style.dot_ratio * 0.45
            cy = top + style.h - r
            draw.ellipse([cx - r, cy - r, cx + r, cy + r], fill=255)
            x += pitch * style.dot_ratio
            continue
        if ch == " ":
            x += pitch
            continue
        bb = f.getbbox(ch)
        cw = bb[2] - bb[0]
        dx = x + (pitch - cw) / 2 - bb[0] + random.gauss(0, pitch * 0.02)
        dy = top - style.cap_top + random.gauss(0, style.h * 0.015)
        tb = draw.textbbox((dx, dy), ch, font=f)
        draw.text((dx, dy), ch, font=f, fill=255)
        if ch in CHARSET:
            boxes.append([CHARSET.index(ch), tb[0] - pad, tb[1] - pad, tb[2] + pad, tb[3] + pad])
        x += pitch
    return x


def rub_smear(layer, h):
    """인쇄 문질림: 일부 지움 + 한쪽으로 끌린 번짐. layer: float(0~1) 2D."""
    H, W = layer.shape
    out = layer.copy()
    if random.random() < 0.6:  # 지워짐 (긁힌 줄무늬)
        streak = Image.new("L", (W, H), 0)
        d = ImageDraw.Draw(streak)
        for _ in range(random.randint(1, 3)):
            ang = math.radians(random.choice([random.uniform(-15, 15), random.uniform(20, 70), random.uniform(-70, -20)]))
            cx, cy = random.uniform(0, W), random.uniform(0, H)
            L = max(W, H)
            wdt = max(2, int(h * random.uniform(0.12, 0.5)))
            d.line([(cx - L * math.cos(ang), cy - L * math.sin(ang)), (cx + L * math.cos(ang), cy + L * math.sin(ang))],
                   fill=255, width=wdt)
        streak = streak.filter(ImageFilter.GaussianBlur(h * random.uniform(0.05, 0.2)))
        s = np.asarray(streak, np.float32) / 255.0
        out = out * (1 - random.uniform(0.4, 0.85) * s)
    if random.random() < 0.6:  # 끌림 꼬리
        ang = math.radians(random.choice([random.uniform(-12, 12), 180 + random.uniform(-12, 12), random.uniform(0, 360)]))
        length = h * random.uniform(0.2, 0.9)
        n = max(2, int(length / 1.5))
        tail = np.zeros_like(out)
        for i in range(1, n + 1):
            t = i / n
            sx, sy = int(round(length * t * math.cos(ang))), int(round(length * t * math.sin(ang)))
            tail = np.maximum(tail, shift(layer, sx, sy) * (1 - t) * random.uniform(0.35, 0.7))
        region = np.asarray(Image.fromarray((gs.smooth_noise(W, H, max(2, int(h))) * 255).astype(np.uint8)), np.float32) / 255.0
        region = (region > random.uniform(0.3, 0.6)).astype(np.float32)
        out = np.maximum(out, tail * region)
    if random.random() < 0.3:  # 잉크 빠짐 (점점이)
        n = gs.smooth_noise(W, H, max(2, int(h / 6)))
        out = out * np.clip((n - random.uniform(0.1, 0.35)) * 4, 0, 1)
    return out


def shift(a, dx, dy):
    H, W = a.shape
    out = np.zeros_like(a)
    xs0, xs1 = max(0, -dx), min(W, W - dx)
    ys0, ys1 = max(0, -dy), min(H, H - dy)
    if xs1 > xs0 and ys1 > ys0:
        out[ys0 + dy:ys1 + dy, xs0 + dx:xs1 + dx] = a[ys0:ys1, xs0:xs1]
    return out


def motion_blur(img, length, ang_deg):
    """img: float 2D. length(px) 방향 ang 로 평균."""
    if length < 2:
        return img
    ang = math.radians(ang_deg)
    n = max(2, int(length))
    pad = int(length) + 2
    p = np.pad(img, pad, mode="edge")
    acc = np.zeros_like(p)
    for t in np.linspace(-length / 2, length / 2, n):
        acc += np.roll(np.roll(p, int(round(t * math.cos(ang))), axis=1), int(round(t * math.sin(ang))), axis=0)
    return (acc / n)[pad:-pad, pad:-pad]


# ---------------------------------------------------------------- 장면 구성
def draw_box_text(text_mask, boxes, box, style, korean, texts=None, rub_p=0.3, x_jitter=45):
    """박스 하나에 (한글 라벨 +) 코드 2줄(가끔 1/3줄)을 찍는다. texts 를 주면 그 내용을 찍는다."""
    x0, y0, x1, y1 = box["rect"]
    h = style.h
    pitch = h * style.pitch_ratio
    if texts is None:
        n_lines = random.choices([1, 2, 3], weights=[1, 8, 1])[0]
        # 30% 는 날짜 대신 제조번호를 한 줄 더 (영문 비중을 높임)
        second = random_lot() if random.random() < 0.3 else random_date()
        texts = [random_lot(), second, random_lot()][:n_lines]
        if n_lines == 1:
            texts = [random.choice([random_lot(), random_date()])]
    n_lines = len(texts)
    line_gap = (box["tops"][1] - box["tops"][0]) if "tops" in box else h * random.uniform(1.2, 1.5)
    line_gap *= random.uniform(0.95, 1.08)
    top0 = box["tops"][0] + random.uniform(-4, 4)
    if n_lines == 3:
        top0 -= line_gap / 2
    x = box["x"] + random.uniform(0, x_jitter)

    W, H = x1 - x0, y1 - y0
    layer_img = Image.new("L", (W, H), 0)
    d = ImageDraw.Draw(layer_img)
    local = []
    lx = x - x0
    avail = W - 10 - lx
    for i, t in enumerate(texts):
        units = sum(style.dot_ratio if c == "." else 1 for c in t)
        p = pitch
        if units * p > avail:  # 길면 자간을 줄이고, 그래도 넘치면 뒤를 자름 (라벨 쪽으로 밀지 않음)
            p = max(h * 0.8, avail / units)
            while t and sum(style.dot_ratio if c == "." else 1 for c in t) * p > avail:
                t = t[:-1]
        if t:
            render_line(d, t, lx, top0 - y0 + i * line_gap, style, p, local)
    if style.k > 1:
        layer_img = layer_img.filter(ImageFilter.MaxFilter(style.k))
    layer = np.asarray(layer_img, np.float32) / 255.0
    if random.random() < rub_p:
        layer = rub_smear(layer, h)
    sub = text_mask[y0:y1, x0:x1]
    np.maximum(sub, layer[: sub.shape[0], : sub.shape[1]], out=sub)
    for c, a, b, cc, dd in local:
        boxes.append([c, a + x0, b + y0, cc + x0, dd + y0])

    if korean is not None:  # 한글 라벨 (라벨링하지 않는 방해 글자)
        kf, labels, kimg = korean
        kd = ImageDraw.Draw(kimg)
        for i, lab in enumerate(labels[:len(texts)]):
            kd.text((box["label_x"], top0 + i * line_gap + h * 0.1), lab, font=kf, fill=int(random.uniform(200, 250)))


def plate_scene():
    base = _G["plate"]
    img = base * random.uniform(0.75, 1.25) + random.uniform(-15, 15)
    H, W = img.shape
    text_mask = np.zeros((H, W), np.float32)
    boxes = []
    style = CodeStyle(h=random.uniform(25, 35))
    for b in _G["plate_boxes"]:
        if random.random() < 0.95:
            draw_box_text(text_mask, boxes, b, style, None)
    return img, text_mask, boxes, style, [b["rect"] for b in _G["plate_boxes"]]


def synth_scene():
    W, H = random.randint(700, 1400), random.randint(400, 1100)
    bgv = random.uniform(5, 45)
    img = bgv * (0.5 + gs.smooth_noise(W, H, random.choice([2, 3, 4])))
    rows, cols = random.randint(1, 5), random.choice([1, 1, 2, 2, 2])
    h = random.uniform(18, 42)
    margin = random.uniform(20, 80)
    gx0, gy0 = margin, margin
    cell_w, cell_h = (W - 2 * margin) / cols, (H - 2 * margin) / rows
    box_h = min(cell_h - 4, h * random.uniform(3.6, 5.0))
    style = CodeStyle(h=h)
    korean_ok = _G["ko_fonts"] and random.random() < 0.85
    kfont = ImageFont.truetype(random.choice(_G["ko_fonts"]), int(h * random.uniform(0.75, 0.95))) if korean_ok else None
    labels = random.choice(KOREAN_LABELS)
    pil = Image.fromarray(np.clip(img, 0, 255).astype(np.uint8))
    d = ImageDraw.Draw(pil)
    rects, specs = [], []
    for r in range(rows):
        for c in range(cols):
            x0 = int(gx0 + c * cell_w + random.uniform(-8, 8))
            y0 = int(gy0 + r * cell_h + random.uniform(-4, 4))
            x1 = int(x0 + cell_w - random.uniform(3, 12))
            y1 = int(y0 + box_h)
            if x1 - x0 < h * 8 or y1 > H - 2 or x1 > W - 2:
                continue
            face = random.uniform(30, 100)
            d.rectangle([x0, y0, x1, y1], fill=int(face))
            hl = random.uniform(0, 40)  # 광택 띠
            hy = random.uniform(y0, y1)
            for k in range(int(box_h * 0.3)):
                v = int(face + hl * (1 - k / (box_h * 0.3)))
                d.line([(x0, hy - k), (x1, hy - k)], fill=v)
            d.rectangle([x0, y0, x1, y1], outline=int(random.uniform(110, 230)), width=random.randint(1, 3))
            rects.append([x0, y0, x1, y1])
            label_w = (kfont.getlength(labels[0]) + h) if kfont else 0
            specs.append({"rect": [x0, y0, x1, y1], "label_x": x0 + h * 0.8,
                          "x": x0 + h * 0.8 + label_w + random.uniform(0, h * 2),
                          "tops": [y0 + box_h / 2 - h * 1.3, y0 + box_h / 2 + h * 0.1], "h": h})
    pil = pil.filter(ImageFilter.GaussianBlur(random.uniform(0, 1.2)))
    img = np.asarray(pil, np.float32)
    text_mask = np.zeros((H, W), np.float32)
    boxes = []
    kimg = Image.new("L", (W, H), 0) if kfont else None
    for s in specs:
        draw_box_text(text_mask, boxes, s, style, (kfont, labels, kimg) if kfont else None)
    if kimg is not None:
        k = np.asarray(kimg, np.float32)
        img = np.maximum(img, k * (k > 0))
    return img, text_mask, boxes, style, rects


def compose(img, text_mask, style):
    a = np.clip(text_mask * style.alpha, 0, 1)
    glow = np.asarray(Image.fromarray((a * 255).astype(np.uint8)).filter(
        ImageFilter.GaussianBlur(style.h * 0.08)), np.float32) / 255.0
    out = img * (1 - a) + style.color * a
    out = out + glow * random.uniform(0, 35)
    return out


def pick_crop(W, H, rects):
    k = random.random()
    if k < 0.45 and rects:  # 박스 하나
        r = random.choice(rects)
    elif k < 0.75 and rects:  # 이웃 박스 여러 개
        r0 = random.choice(rects)
        sel = [q for q in rects if abs(q[1] - r0[1]) < (r0[3] - r0[1]) * random.uniform(1.0, 3.0) * 1.2]
        r = [min(q[0] for q in sel), min(q[1] for q in sel), max(q[2] for q in sel), max(q[3] for q in sel)]
    else:
        r = [0, 0, W, H]
    m = random.uniform(0, 40)
    x0, y0 = int(max(0, r[0] - m * random.random())), int(max(0, r[1] - m * random.random()))
    x1, y1 = int(min(W, r[2] + m * random.random())), int(min(H, r[3] + m * random.random()))
    return x0, y0, x1, y1


def make_label_sample(kind):
    img, text_mask, boxes, style, rects = plate_scene() if kind == "plate" else synth_scene()
    H, W = img.shape
    out = compose(img, text_mask, style)
    x0, y0, x1, y1 = pick_crop(W, H, rects)

    blur_len, blur_ang = 0.0, 0.0
    if random.random() < 0.4:  # 움직임 번짐 (이미지 쓸림)
        # 컨베이어 이동 방향(가로)은 길게, 그 외 방향은 짧게
        k = random.choices(["h", "v", "any"], weights=[8, 1, 1])[0]
        if k == "h":
            blur_len = style.h * random.choice([random.uniform(0.1, 0.35), random.uniform(0.1, 0.35), random.uniform(0.35, 0.6)])
            blur_ang = random.uniform(-4, 4)
        elif k == "v":
            blur_len = style.h * random.uniform(0.1, 0.3)
            blur_ang = 90 + random.uniform(-4, 4)
        else:
            blur_len = style.h * random.uniform(0.1, 0.35)
            blur_ang = random.uniform(0, 180)
        m = int(blur_len) + 4
        cx0, cy0, cx1, cy1 = max(0, x0 - m), max(0, y0 - m), min(W, x1 + m), min(H, y1 + m)
        region = motion_blur(out[cy0:cy1, cx0:cx1], blur_len, blur_ang)
        out = out.copy()
        out[cy0:cy1, cx0:cx1] = region
    crop = out[y0:y1, x0:x1]

    ex = abs(math.cos(math.radians(blur_ang))) * blur_len * 0.3
    ey = abs(math.sin(math.radians(blur_ang))) * blur_len * 0.3
    kept = []
    for c, a, b, cc, dd in boxes:
        a, b, cc, dd = a - ex - x0, b - ey - y0, cc + ex - x0, dd + ey - y0
        ca, cb, ccc, cdd = max(0, a), max(0, b), min(x1 - x0, cc), min(y1 - y0, dd)
        if ccc <= ca or cdd <= cb:
            continue
        if (ccc - ca) * (cdd - cb) < 0.6 * (cc - a) * (dd - b):
            continue  # 크롭 경계에 걸린 글자는 라벨 제외
        kept.append([c, ca, cb, ccc, cdd])

    # 촬영 조건
    g = random.uniform(0.7, 1.4)
    crop = 255.0 * (np.clip(crop, 0, 255) / 255.0) ** g
    crop = crop * random.uniform(0.8, 1.2) + random.uniform(-20, 20)
    if random.random() < 0.3:
        crop = np.asarray(Image.fromarray(np.clip(crop, 0, 255).astype(np.uint8)).filter(
            ImageFilter.GaussianBlur(random.uniform(0.3, 1.2))), np.float32)
    crop = crop + np.random.normal(0, random.uniform(1, 8), crop.shape)
    pil = Image.fromarray(np.clip(crop, 0, 255).astype(np.uint8)).convert("RGB")
    return pil, kept


def make_any(kind):
    if kind == "generic":
        img, boxes = gs.make_sample(_G["fonts"], CHARSET)
        if img is not None and random.random() < 0.5:
            img = img.convert("L").convert("RGB")
        return img, boxes
    return make_label_sample(kind)


def job(args):
    split, idx, kind, out = args
    for _ in range(20):
        img, boxes = make_any(kind)
        if img is not None and boxes:
            break
    else:
        return None
    w, h = img.size
    name = f"{split}_{kind}_{idx:06d}"
    img.save(Path(out) / "images" / split / f"{name}.jpg", quality=random.randint(85, 98))
    with open(Path(out) / "labels" / split / f"{name}.txt", "w") as f:
        for c, x0, y0, x1, y1 in boxes:
            f.write(f"{c} {(x0 + x1) / 2 / w:.6f} {(y0 + y1) / 2 / h:.6f} {(x1 - x0) / w:.6f} {(y1 - y0) / h:.6f}\n")
    return name


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default="datasets/label")
    ap.add_argument("--train", type=int, default=4000)
    ap.add_argument("--val", type=int, default=400)
    ap.add_argument("--mix", default="0.55,0.30,0.15", help="실제바탕,합성박스,범용 비율")
    ap.add_argument("--fonts-dir", default=None)
    ap.add_argument("--workers", type=int, default=4)
    ap.add_argument("--seed", type=int, default=0)
    args = ap.parse_args()

    out = Path(args.out).resolve()
    mix = [float(v) for v in args.mix.split(",")]
    kinds = ["plate", "synth", "generic"]
    jobs = []
    rng = random.Random(args.seed)
    for split, count in (("train", args.train), ("val", args.val)):
        (out / "images" / split).mkdir(parents=True, exist_ok=True)
        (out / "labels" / split).mkdir(parents=True, exist_ok=True)
        for i in range(count):
            jobs.append((split, i, rng.choices(kinds, weights=mix)[0], str(out)))

    with Pool(args.workers, initializer=init_worker, initargs=(args.fonts_dir, args.seed)) as pool:
        done = 0
        for _ in pool.imap_unordered(job, jobs, chunksize=8):
            done += 1
            if done % 500 == 0:
                print(f"{done}/{len(jobs)}", flush=True)

    with open(out / "data.yaml", "w", encoding="utf-8") as f:
        f.write(f"path: {out.as_posix()}\ntrain: images/train\nval: images/val\nnames:\n")
        for k, c in enumerate(CHARSET):
            f.write(f"  {k}: '{c}'\n")
    print(f"완료: {out / 'data.yaml'}")


if __name__ == "__main__":
    main()
