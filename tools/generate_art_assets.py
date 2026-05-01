from __future__ import annotations

import math
import random
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont


ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / "Assets" / "Art"

INK = (30, 24, 22, 255)
INNER = (64, 48, 41, 255)
SKIN = (205, 155, 115, 255)
SKIN_DARK = (143, 95, 72, 255)
OFFWHITE = (218, 202, 172, 255)
BROWN = (97, 64, 43, 255)
DARK_BROWN = (58, 43, 34, 255)
SIENNA = (142, 73, 43, 255)
RED = (143, 48, 42, 255)
GOLD = (184, 136, 66, 255)
GREEN = (73, 101, 70, 255)
BLUE = (58, 78, 107, 255)
PURPLE = (82, 67, 105, 255)
STONE = (112, 105, 95, 255)


def font(size: int, bold: bool = False) -> ImageFont.FreeTypeFont | ImageFont.ImageFont:
    candidates = [
        "C:/Windows/Fonts/arialbd.ttf" if bold else "C:/Windows/Fonts/arial.ttf",
        "C:/Windows/Fonts/msjhbd.ttc" if bold else "C:/Windows/Fonts/msjh.ttc",
    ]
    for candidate in candidates:
        try:
            return ImageFont.truetype(candidate, size=size)
        except OSError:
            pass
    return ImageFont.load_default()


def save(img: Image.Image, rel: str) -> Path:
    path = ART / rel
    path.parent.mkdir(parents=True, exist_ok=True)
    img.save(path)
    return path


def line(draw: ImageDraw.ImageDraw, points, fill=INK, width=5):
    draw.line(points, fill=fill, width=width, joint="curve")


def ellipse(draw, box, fill, outline=INK, width=5):
    draw.ellipse(box, fill=fill, outline=outline, width=width)


def rect(draw, box, fill, outline=INK, width=5, radius=0):
    if radius:
        draw.rounded_rectangle(box, radius=radius, fill=fill, outline=outline, width=width)
    else:
        draw.rectangle(box, fill=fill, outline=outline, width=width)


def poly(draw, points, fill, outline=INK, width=5):
    draw.polygon(points, fill=fill)
    draw.line(points + [points[0]], fill=outline, width=width, joint="curve")


def add_grime(draw, seed: int, box: tuple[int, int, int, int], count: int = 18):
    rng = random.Random(seed)
    for _ in range(count):
        x = rng.randint(box[0], box[2])
        y = rng.randint(box[1], box[3])
        r = rng.randint(1, 4)
        color = rng.choice([(46, 34, 28, 80), (220, 196, 150, 90), (104, 58, 42, 100)])
        draw.ellipse((x - r, y - r, x + r, y + r), fill=color)


def draw_chibi(
    name: str,
    role: str,
    rel: str,
    *,
    palette: tuple[tuple[int, int, int, int], ...],
    hair: tuple[int, int, int, int],
    accessory: str,
    pose: str = "neutral",
    species: str = "human",
    wounded: bool = False,
    writing: bool = False,
):
    img = Image.new("RGBA", (512, 768), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    seed = abs(hash(rel)) & 0xFFFFFFFF
    rng = random.Random(seed)

    # Ground shadow, kept small and transparent-friendly.
    d.ellipse((165, 690, 347, 728), fill=(20, 15, 12, 38))

    x = 256
    head_top = 74
    head_w = 182 if species != "dwarf" else 192
    head_h = 176 if species != "orc" else 188
    body_top = 260
    body_h = 280 if species != "dwarf" else 235
    body_w = 150 if species != "orc" else 172

    if pose == "leaving":
        x -= 18
    if pose == "waiting":
        body_top += 8

    # Legs and boots.
    leg_y = body_top + body_h - 25
    rect(d, (x - 72, leg_y, x - 25, leg_y + 130), DARK_BROWN, width=5, radius=12)
    rect(d, (x + 25, leg_y, x + 72, leg_y + 130), DARK_BROWN, width=5, radius=12)
    rect(d, (x - 88, leg_y + 112, x - 17, leg_y + 145), BROWN, width=5, radius=13)
    rect(d, (x + 17, leg_y + 112, x + 88, leg_y + 145), BROWN, width=5, radius=13)

    # Body cloak/tunic.
    main, trim, shadow = palette
    poly(
        d,
        [
            (x - body_w // 2, body_top + 22),
            (x + body_w // 2, body_top + 22),
            (x + body_w // 2 + 28, body_top + body_h),
            (x - body_w // 2 - 28, body_top + body_h),
        ],
        main,
        width=6,
    )
    poly(
        d,
        [
            (x + 5, body_top + 34),
            (x + body_w // 2, body_top + 40),
            (x + body_w // 2 + 20, body_top + body_h - 10),
            (x + 5, body_top + body_h - 22),
        ],
        shadow,
        outline=INNER,
        width=3,
    )
    rect(d, (x - 88, body_top + 116, x + 88, body_top + 148), trim, width=4, radius=12)

    # Arms.
    arm_left = [(x - 78, body_top + 74), (x - 150, body_top + 190), (x - 122, body_top + 215), (x - 55, body_top + 105)]
    arm_right = [(x + 78, body_top + 74), (x + 150, body_top + 190), (x + 122, body_top + 215), (x + 55, body_top + 105)]
    if writing:
        arm_right = [(x + 72, body_top + 92), (x + 95, body_top + 188), (x + 61, body_top + 202), (x + 36, body_top + 112)]
    poly(d, arm_left, main, width=5)
    poly(d, arm_right, shadow, width=5)
    ellipse(d, (x - 158, body_top + 184, x - 116, body_top + 226), SKIN, width=4)
    ellipse(d, (x + 112, body_top + 184, x + 154, body_top + 226), SKIN, width=4)

    # Head, ears, hair.
    if species == "elf":
        poly(d, [(x - 91, 160), (x - 146, 138), (x - 100, 198)], SKIN, width=4)
        poly(d, [(x + 91, 160), (x + 146, 138), (x + 100, 198)], SKIN, width=4)
    if species == "orc":
        ellipse(d, (x - 120, 140, x - 90, 185), SKIN_DARK, width=4)
        ellipse(d, (x + 90, 140, x + 120, 185), SKIN_DARK, width=4)
    ellipse(d, (x - head_w // 2, head_top, x + head_w // 2, head_top + head_h), SKIN_DARK if species == "orc" else SKIN, width=6)
    poly(
        d,
        [
            (x - head_w // 2 + 7, head_top + 65),
            (x - 35, head_top + 14),
            (x + 55, head_top + 20),
            (x + head_w // 2 - 6, head_top + 74),
            (x + 75, head_top + 124),
            (x - 65, head_top + 104),
        ],
        hair,
        width=5,
    )
    for i in range(5):
        sx = x - 72 + i * 36
        poly(d, [(sx, head_top + 66), (sx + 32, head_top + 28), (sx + 44, head_top + 86)], hair, width=3)

    # Face.
    eye_y = head_top + 94
    eye_dx = 38
    d.ellipse((x - eye_dx - 12, eye_y - 12, x - eye_dx + 12, eye_y + 16), fill=INK)
    d.ellipse((x + eye_dx - 12, eye_y - 12, x + eye_dx + 12, eye_y + 16), fill=INK)
    d.ellipse((x - eye_dx - 5, eye_y - 8, x - eye_dx + 3, eye_y), fill=OFFWHITE)
    d.ellipse((x + eye_dx - 5, eye_y - 8, x + eye_dx + 3, eye_y), fill=OFFWHITE)
    line(d, [(x - 18, eye_y + 44), (x + 18, eye_y + 44)], width=4)
    if wounded:
        line(d, [(x + 18, eye_y + 18), (x + 58, eye_y + 42)], fill=RED, width=5)
        rect(d, (x - 78, body_top + 25, x - 24, body_top + 52), OFFWHITE, width=3, radius=6)
    if role == "ophelia":
        line(d, [(x - 62, eye_y - 23), (x - 14, eye_y - 28)], width=4)
        line(d, [(x + 14, eye_y - 28), (x + 62, eye_y - 23)], width=4)

    # Accessories and profession cues.
    if accessory == "sword":
        line(d, [(x + 134, body_top + 12), (x + 190, body_top + 290)], fill=INK, width=8)
        line(d, [(x + 126, body_top + 84), (x + 176, body_top + 68)], fill=GOLD, width=8)
    elif accessory == "staff":
        line(d, [(x - 158, body_top + 10), (x - 185, body_top + 335)], fill=DARK_BROWN, width=9)
        ellipse(d, (x - 205, body_top - 12, x - 166, body_top + 28), GOLD, width=4)
    elif accessory == "bow":
        d.arc((x - 196, body_top + 10, x - 118, body_top + 280), 255, 105, fill=INK, width=7)
        line(d, [(x - 159, body_top + 22), (x - 159, body_top + 270)], fill=OFFWHITE, width=2)
    elif accessory == "emblem":
        ellipse(d, (x - 26, body_top + 76, x + 26, body_top + 128), GOLD, width=4)
        line(d, [(x, body_top + 86), (x, body_top + 118)], fill=OFFWHITE, width=5)
        line(d, [(x - 14, body_top + 102), (x + 14, body_top + 102)], fill=OFFWHITE, width=5)
    elif accessory == "pocket_pen":
        rect(d, (x + 44, body_top + 90, x + 76, body_top + 130), trim, width=3, radius=4)
        line(d, [(x + 57, body_top + 84), (x + 70, body_top + 126)], fill=OFFWHITE, width=4)
    elif accessory == "wrist_mark":
        line(d, [(x - 150, body_top + 206), (x - 119, body_top + 190)], fill=PURPLE, width=5)
    elif accessory == "paper":
        rect(d, (x + 60, body_top + 184, x + 145, body_top + 250), OFFWHITE, width=4, radius=3)
        for yy in range(body_top + 202, body_top + 238, 12):
            line(d, [(x + 73, yy), (x + 132, yy)], fill=(90, 68, 55, 190), width=2)

    add_grime(d, seed, (x - 95, body_top + 40, x + 95, body_top + body_h), count=18)
    save(img, rel)


def prop_canvas(size=(512, 512)):
    return Image.new("RGBA", size, (0, 0, 0, 0)), None


def draw_chair():
    img = Image.new("RGBA", (512, 512), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.ellipse((130, 430, 382, 470), fill=(20, 15, 12, 35))
    rect(d, (130, 118, 382, 310), BROWN, width=7, radius=16)
    rect(d, (156, 145, 356, 285), (116, 74, 46, 255), width=4, radius=10)
    rect(d, (106, 285, 406, 380), DARK_BROWN, width=7, radius=18)
    for x in (145, 350):
        rect(d, (x, 370, x + 34, 455), BROWN, width=5, radius=8)
    for x in range(170, 350, 42):
        line(d, [(x, 126), (x - 8, 294)], fill=INK, width=4)
    add_grime(d, 10, (130, 120, 382, 380), 28)
    save(img, "Props/chair_ophelia.png")


def draw_teacup(rel: str, state: str):
    img = Image.new("RGBA", (512, 512), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.ellipse((135, 372, 378, 415), fill=(20, 15, 12, 35))
    rect(d, (150, 185, 345, 330), OFFWHITE, width=7, radius=28)
    d.arc((314, 210, 410, 300), -70, 80, fill=INK, width=8)
    d.arc((330, 227, 385, 284), -70, 80, fill=INK, width=5)
    d.ellipse((150, 170, 345, 220), fill=OFFWHITE, outline=INK, width=7)
    liquid = {"full": SIENNA, "residue": (92, 48, 34, 255), "empty": (226, 214, 191, 255)}[state]
    d.ellipse((172, 184, 323, 208), fill=liquid, outline=INNER, width=2)
    if state == "full":
        for sx in (205, 245, 285):
            line(d, [(sx, 155), (sx + 8, 120), (sx - 2, 92)], fill=(95, 76, 55, 90), width=3)
    elif state == "residue":
        for _ in range(14):
            x = random.randint(190, 310)
            y = random.randint(190, 204)
            d.ellipse((x - 3, y - 2, x + 3, y + 2), fill=(48, 32, 24, 150))
    add_grime(d, len(rel), (160, 190, 340, 330), 12)
    save(img, rel)


def draw_bulletin_board():
    img = Image.new("RGBA", (1024, 768), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    rect(d, (95, 85, 929, 650), (105, 67, 39, 255), width=10, radius=12)
    rect(d, (140, 128, 884, 604), (134, 91, 54, 255), width=6, radius=6)
    rng = random.Random(33)
    for i in range(15):
        w, h = rng.randint(95, 165), rng.randint(80, 140)
        x, y = rng.randint(170, 780), rng.randint(160, 480)
        c = rng.choice([OFFWHITE, (193, 169, 123, 255), (160, 126, 87, 255)])
        angle = rng.uniform(-5, 5)
        note = Image.new("RGBA", (w, h), (0, 0, 0, 0))
        nd = ImageDraw.Draw(note)
        rect(nd, (4, 4, w - 5, h - 5), c, width=3)
        for yy in range(22, h - 14, 15):
            line(nd, [(18, yy), (w - 20, yy)], fill=(83, 61, 47, 150), width=2)
        ellipse(nd, (w // 2 - 6, 8, w // 2 + 6, 20), RED, width=2)
        note = note.rotate(angle, expand=True, resample=Image.Resampling.BICUBIC)
        img.alpha_composite(note, (x, y))
    add_grime(d, 44, (120, 100, 900, 630), 80)
    save(img, "Props/bulletin_board.png")


def draw_ledger():
    img = Image.new("RGBA", (512, 512), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.ellipse((130, 410, 390, 450), fill=(20, 15, 12, 35))
    rect(d, (130, 92, 382, 405), DARK_BROWN, width=8, radius=18)
    rect(d, (158, 120, 354, 376), (128, 80, 48, 255), width=4, radius=8)
    rect(d, (128, 92, 180, 405), (63, 45, 34, 255), width=6, radius=14)
    ellipse(d, (230, 226, 284, 280), GOLD, width=4)
    line(d, [(257, 238), (257, 270)], fill=OFFWHITE, width=5)
    line(d, [(241, 254), (273, 254)], fill=OFFWHITE, width=5)
    add_grime(d, 51, (130, 92, 382, 405), 24)
    save(img, "Props/guild_ledger.png")


def draw_sacred_stone(rel: str, stage: int):
    img = Image.new("RGBA", (512, 512), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.ellipse((130, 405, 386, 450), fill=(20, 15, 12, 35))
    if stage == 5:
        pieces = [
            [(210, 170), (286, 140), (318, 250), (240, 275)],
            [(180, 282), (250, 280), (228, 380), (152, 360)],
            [(292, 270), (372, 310), (330, 390), (260, 350)],
        ]
        for pts in pieces:
            poly(d, pts, STONE, width=7)
    else:
        poly(d, [(256, 95), (363, 185), (330, 382), (185, 390), (146, 196)], STONE, width=8)
        glow = [GREEN, GOLD, RED, PURPLE][min(stage - 1, 3)]
        for r in range(0, stage + 2):
            d.arc((196 - r * 5, 165 - r * 4, 316 + r * 5, 316 + r * 4), 205, 335, fill=glow, width=5)
        ellipse(d, (230, 225, 282, 277), glow, width=4)
    add_grime(d, 60 + stage, (150, 110, 370, 390), 30)
    save(img, rel)


def draw_letters():
    img = Image.new("RGBA", (512, 512), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.ellipse((116, 405, 392, 448), fill=(20, 15, 12, 35))
    rng = random.Random(70)
    for i in range(7):
        w, h = rng.randint(130, 190), rng.randint(75, 115)
        sheet = Image.new("RGBA", (w, h), (0, 0, 0, 0))
        sd = ImageDraw.Draw(sheet)
        rect(sd, (3, 3, w - 4, h - 4), rng.choice([OFFWHITE, (196, 173, 132, 255)]), width=3)
        line(sd, [(16, 28), (w - 20, 28)], fill=(85, 65, 50, 140), width=2)
        line(sd, [(16, 46), (w - 36, 46)], fill=(85, 65, 50, 120), width=2)
        ellipse(sd, (w - 42, h - 36, w - 18, h - 12), RED, width=2)
        sheet = sheet.rotate(rng.uniform(-20, 20), expand=True, resample=Image.Resampling.BICUBIC)
        img.alpha_composite(sheet, (rng.randint(115, 250), rng.randint(160, 320)))
    save(img, "Props/mira_letters_pile.png")


def draw_note(rel: str, mood: str):
    img = Image.new("RGBA", (512, 768), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    bg = {"light": (218, 202, 162, 255), "dark": (152, 120, 89, 255), "neutral": (195, 174, 136, 255), "door": (174, 143, 98, 255)}[mood]
    rect(d, (112, 72, 400, 680), bg, width=7, radius=4)
    poly(d, [(352, 72), (400, 72), (400, 124)], (120, 88, 64, 255), width=4)
    for yy in range(150, 610, 40):
        x1 = 150 + (yy // 40) % 2 * 8
        line(d, [(x1, yy), (355, yy + random.randint(-2, 2))], fill=(69, 49, 40, 155), width=3)
    for yy in range(180, 620, 80):
        line(d, [(166, yy), (316, yy + 3)], fill=(69, 49, 40, 105), width=2)
    if mood == "dark":
        for _ in range(16):
            x, y = random.randint(125, 380), random.randint(90, 665)
            d.ellipse((x - 4, y - 3, x + 4, y + 3), fill=(48, 31, 24, 90))
    if "door" in rel:
        line(d, [(140, 102), (372, 650)], fill=(90, 58, 39, 95), width=5)
        rect(d, (214, 42, 298, 78), (74, 50, 35, 255), width=5, radius=8)
    save(img, f"Props/Notes/{rel}")


def draw_background(rel: str, stage: int):
    img = Image.new("RGB", (1920, 1080), (46, 36, 31))
    d = ImageDraw.Draw(img)
    top = [(52, 41, 37), (66, 49, 39), (56, 53, 55), (44, 39, 39), (74, 57, 43), (42, 36, 35)][stage]
    bottom = [(122, 83, 48), (134, 88, 48), (95, 82, 73), (71, 57, 56), (65, 48, 46), (98, 77, 55)][stage]
    for y in range(1080):
        t = y / 1079
        c = tuple(int(top[i] * (1 - t) + bottom[i] * t) for i in range(3))
        d.line([(0, y), (1920, y)], fill=c)
    # Floor and beams.
    poly(d, [(0, 780), (1920, 720), (1920, 1080), (0, 1080)], (86, 58, 38), outline=(45, 31, 25), width=8)
    for x in range(-100, 2100, 250):
        line(d, [(x, 1080), (960, 710)], fill=(58, 39, 29), width=7)
    for x in (155, 430, 1490, 1740):
        rect(d, (x, 160, x + 62, 850), (74, 47, 30), width=6)
    for y in (150, 248):
        rect(d, (0, y, 1920, y + 58), (82, 52, 31), width=5)
    # Back counter and guild objects.
    rect(d, (560, 520, 1380, 760), (97, 61, 37), width=8, radius=12)
    rect(d, (620, 575, 1320, 720), (124, 82, 48), width=5, radius=8)
    rect(d, (1190, 250, 1560, 520), (114, 72, 43), width=8, radius=10)
    for i in range(5):
        rect(d, (1220 + i * 62, 290, 1260 + i * 62, 490), (72, 49, 36), width=3, radius=5)
    # Bulletin board and notes.
    rect(d, (270, 230, 650, 520), (108, 68, 39), width=8)
    for i in range(12):
        x = 300 + (i % 4) * 82
        y = 265 + (i // 4) * 78
        rect(d, (x, y, x + 54, y + 48), (185, 155, 106), width=2)
    # Sacred stone changes by stage.
    stone_color = [(132, 132, 103), (149, 136, 78), (152, 92, 72), (99, 75, 111), (94, 88, 83), (92, 84, 74)][stage]
    poly(d, [(960, 360), (1040, 430), (1020, 570), (900, 575), (870, 430)], stone_color, outline=(31, 25, 23), width=8)
    if stage >= 1:
        glow = [(191, 151, 70), (194, 171, 84), (179, 96, 68), (124, 88, 149), (89, 77, 72), (60, 52, 48)][stage]
        for r in range(3 + stage):
            d.arc((905 - r * 5, 405 - r * 5, 1032 + r * 5, 535 + r * 5), 190, 340, fill=glow, width=4)
    # Windows and light.
    for x in (740, 1500):
        rect(d, (x, 190, x + 170, 390), (58, 66, 72), width=7, radius=14)
        line(d, [(x + 85, 190), (x + 85, 390)], fill=(34, 28, 24), width=4)
        line(d, [(x, 290), (x + 170, 290)], fill=(34, 28, 24), width=4)
    if stage in (0, 1):
        overlay = Image.new("RGBA", (1920, 1080), (0, 0, 0, 0))
        od = ImageDraw.Draw(overlay)
        od.polygon([(680, 160), (960, 160), (1260, 870), (650, 870)], fill=(255, 208, 123, 42))
        img = Image.alpha_composite(img.convert("RGBA"), overlay).convert("RGB")
    elif stage >= 4:
        img = img.filter(ImageFilter.GaussianBlur(0.4))
    save(img.convert("RGBA"), f"Backgrounds/{rel}")


def draw_icon(rel: str, kind: str, label: str = ""):
    img = Image.new("RGBA", (256, 256), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.ellipse((30, 30, 226, 226), fill=(82, 55, 38, 255), outline=INK, width=8)
    d.ellipse((48, 48, 208, 208), fill=(135, 91, 54, 255), outline=(53, 39, 32, 255), width=4)
    if kind == "warrior":
        line(d, [(130, 55), (130, 180)], fill=OFFWHITE, width=12)
        line(d, [(90, 108), (170, 108)], fill=GOLD, width=10)
        poly(d, [(130, 40), (150, 70), (110, 70)], OFFWHITE, width=4)
    elif kind == "ranger":
        d.arc((75, 54, 175, 202), 250, 110, fill=OFFWHITE, width=11)
        line(d, [(124, 62), (124, 192)], fill=INK, width=3)
        line(d, [(92, 132), (177, 92)], fill=GOLD, width=8)
    elif kind == "scout":
        poly(d, [(70, 140), (128, 60), (186, 140), (128, 118)], OFFWHITE, width=6)
        line(d, [(128, 118), (128, 190)], fill=OFFWHITE, width=10)
    elif kind == "healer":
        line(d, [(128, 68), (128, 188)], fill=OFFWHITE, width=22)
        line(d, [(78, 128), (178, 128)], fill=OFFWHITE, width=22)
    elif kind == "mage":
        ellipse(d, (92, 62, 164, 134), PURPLE, width=6)
        line(d, [(128, 132), (128, 200)], fill=OFFWHITE, width=10)
        for a in range(0, 360, 60):
            x = 128 + int(math.cos(math.radians(a)) * 66)
            y = 98 + int(math.sin(math.radians(a)) * 46)
            ellipse(d, (x - 6, y - 6, x + 6, y + 6), GOLD, width=2)
    elif kind == "mercenary":
        line(d, [(78, 80), (178, 178)], fill=OFFWHITE, width=10)
        line(d, [(178, 80), (78, 178)], fill=INK, width=10)
        rect(d, (93, 156, 162, 190), GOLD, width=4, radius=8)
    elif kind == "emblem":
        for a in range(0, 360, 45):
            x = 128 + int(math.cos(math.radians(a)) * 68)
            y = 128 + int(math.sin(math.radians(a)) * 68)
            line(d, [(128, 128), (x, y)], fill=GOLD, width=6)
        ellipse(d, (83, 83, 173, 173), OFFWHITE, width=6)
        line(d, [(128, 94), (128, 162)], fill=GOLD, width=8)
        line(d, [(100, 128), (156, 128)], fill=GOLD, width=8)
    elif kind == "rank":
        f = font(98, bold=True)
        bbox = d.textbbox((0, 0), label, font=f)
        d.text(((256 - (bbox[2] - bbox[0])) / 2, 70), label, font=f, fill=OFFWHITE, stroke_width=4, stroke_fill=INK)
    add_grime(d, len(rel), (40, 40, 216, 216), 10)
    save(img, f"UI/Icons/{rel}")


def main():
    characters = [
        ("ophelia_default.png", "ophelia", "adventurer", (BROWN, OFFWHITE, DARK_BROWN), (67, 54, 48, 255), "sword"),
        ("ophelia_wounded.png", "ophelia", "adventurer", (BROWN, OFFWHITE, DARK_BROWN), (67, 54, 48, 255), "sword"),
        ("Staff/mira_default.png", "mira", "staff", (BLUE, GOLD, (42, 51, 72, 255)), (83, 63, 45, 255), "emblem"),
        ("Staff/mira_stage4_light_accepted.png", "mira", "staff", (BLUE, GOLD, (42, 51, 72, 255)), (83, 63, 45, 255), "emblem"),
        ("Staff/mira_stage4_common_resigned.png", "mira", "staff", ((68, 76, 88, 255), OFFWHITE, (48, 52, 58, 255)), (83, 63, 45, 255), "emblem"),
        ("Staff/tan_default.png", "tan", "staff", (GREEN, OFFWHITE, (45, 69, 49, 255)), (52, 43, 35, 255), "pocket_pen"),
        ("Staff/tan_stage5_writing.png", "tan", "staff", (GREEN, OFFWHITE, (45, 69, 49, 255)), (52, 43, 35, 255), "paper"),
        ("Staff/kaira_default.png", "kaira", "staff", (PURPLE, OFFWHITE, (53, 42, 66, 255)), (47, 42, 52, 255), "wrist_mark"),
        ("Staff/kaira_stage4_leaving.png", "kaira", "staff", (PURPLE, OFFWHITE, (53, 42, 66, 255)), (47, 42, 52, 255), "wrist_mark"),
        ("Staff/kaira_stage5_waiting.png", "kaira", "staff", ((66, 60, 78, 255), OFFWHITE, (48, 43, 57, 255)), (47, 42, 52, 255), "paper"),
    ]
    for rel, role, group, pal, hair, acc in characters:
        target = f"Characters/Adventurers/{rel}" if not rel.startswith("Staff/") else f"Characters/{rel}"
        draw_chibi(role, role, target, palette=pal, hair=hair, accessory=acc, wounded="wounded" in rel, writing="writing" in rel, pose="leaving" if "leaving" in rel else "waiting" if "waiting" in rel else "neutral")

    named = [
        ("adv_101_marcus_kurt.png", "marcus", "mercenary", "human", (BROWN, GOLD, DARK_BROWN), "sword"),
        ("adv_102_elia_vienne.png", "elia", "mage", "human", (PURPLE, OFFWHITE, (55, 45, 74, 255)), "staff"),
        ("adv_103_kamon_grey.png", "kamon", "warrior", "orc", (SIENNA, GOLD, (91, 50, 36, 255)), "sword"),
        ("adv_104_lucy_fane.png", "lucy", "ranger", "elf", (GREEN, OFFWHITE, (45, 72, 51, 255)), "bow"),
        ("adv_105_marco_seya.png", "marco", "mercenary", "human", ((93, 71, 53, 255), GOLD, DARK_BROWN), "sword"),
        ("adv_106_lannis_cohen.png", "lannis", "healer", "human", (OFFWHITE, GOLD, (178, 164, 134, 255)), "emblem"),
        ("adv_107_victor_dunn.png", "victor", "warrior", "dwarf", (BROWN, GOLD, DARK_BROWN), "sword"),
        ("adv_108_liliana_sill.png", "liliana", "ranger", "elf", (GREEN, GOLD, (44, 68, 50, 255)), "bow"),
        ("adv_109_michelle_ash.png", "michelle", "mage", "human", (PURPLE, GOLD, (53, 43, 70, 255)), "staff"),
        ("adv_110_maude_clay.png", "maude", "mercenary", "human", (DARK_BROWN, RED, (42, 34, 30, 255)), "sword"),
        ("adv_111_evan_ross.png", "evan", "scout", "human", ((62, 65, 58, 255), OFFWHITE, (40, 43, 39, 255)), "pocket_pen"),
    ]
    hair_colors = [(65, 52, 42, 255), (88, 68, 46, 255), (46, 42, 38, 255), (126, 98, 66, 255)]
    for i, (fname, name, role, species, pal, acc) in enumerate(named):
        draw_chibi(name, role, f"Characters/Adventurers/{fname}", palette=pal, hair=hair_colors[i % len(hair_colors)], accessory=acc, species=species, wounded=name == "maude")

    generic = [
        ("adv_generic_warrior.png", "warrior", (BROWN, GOLD, DARK_BROWN), "sword"),
        ("adv_generic_mage.png", "mage", (PURPLE, OFFWHITE, (55, 45, 74, 255)), "staff"),
        ("adv_generic_ranger.png", "ranger", (GREEN, OFFWHITE, (45, 72, 51, 255)), "bow"),
        ("adv_generic_scout.png", "scout", ((62, 65, 58, 255), OFFWHITE, (40, 43, 39, 255)), "pocket_pen"),
        ("adv_generic_healer.png", "healer", (OFFWHITE, GOLD, (178, 164, 134, 255)), "emblem"),
        ("adv_generic_mercenary.png", "mercenary", (SIENNA, GOLD, DARK_BROWN), "sword"),
    ]
    for fname, role, pal, acc in generic:
        draw_chibi(role, role, f"Characters/Adventurers/{fname}", palette=pal, hair=(64, 51, 42, 255), accessory=acc)

    draw_chair()
    draw_teacup("Props/mug_full.png", "full")
    draw_teacup("Props/mug_residue.png", "residue")
    draw_teacup("Props/mug_empty.png", "empty")
    draw_teacup("Props/teacup_hot.png", "full")
    draw_teacup("Props/teacup_cold.png", "residue")
    draw_teacup("Props/teacup_empty.png", "empty")
    draw_bulletin_board()
    draw_ledger()
    for stage in range(1, 6):
        rel = f"Props/SacredStone/sacred_stone_stage{stage}{'_shattered' if stage == 5 else ''}.png"
        draw_sacred_stone(rel, stage)
    draw_letters()

    for rel, mood in [
        ("note_stage1_light.png", "light"),
        ("note_stage1_dark.png", "dark"),
        ("note_stage1_neutral.png", "neutral"),
        ("note_stage3_light.png", "light"),
        ("note_stage3_dark.png", "dark"),
        ("note_stage3_neutral.png", "neutral"),
        ("note_door_stage3_common.png", "door"),
        ("note_door_stage3_dark.png", "dark"),
    ]:
        draw_note(rel, mood)

    for rel, stage in [
        ("bg_guild_onboarding.png", 0),
        ("bg_guild_stage1.png", 1),
        ("bg_guild_stage2.png", 2),
        ("bg_guild_stage3.png", 3),
        ("bg_guild_stage4.png", 4),
        ("bg_guild_stage5.png", 5),
    ]:
        draw_background(rel, stage)

    draw_icon("icon_dawn_emblem.png", "emblem")
    for kind in ["warrior", "ranger", "scout", "healer", "mage", "mercenary"]:
        draw_icon(f"icon_profession_{kind}.png", kind)
    for rank in ["F", "E", "D", "C", "B", "A", "S"]:
        draw_icon(f"icon_rank_{rank}.png", "rank", rank)


if __name__ == "__main__":
    main()
