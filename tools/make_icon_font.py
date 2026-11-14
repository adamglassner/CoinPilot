"""Builds WillowWallet's icon fonts from Microsoft's Fluent UI System Icons (MIT).

Each icon is placed at the code point the app already uses (the old Segoe MDL2 codes), so the app only switches
font families. Output: src/CoinPilot.Desktop/Assets/Fonts/WillowWalletIcons-{Regular,Filled}.ttf

    python tools/make_icon_font.py <path to fluentui-system-icons/fonts>
"""
import json, sys
from fontTools import subset
from fontTools.ttLib import TTFont

ICONS = {  # code point used in the app -> Fluent icon name
    0xE706: 'weather_sunny', 0xE708: 'weather_moon', 0xE70D: 'chevron_down', 0xE70F: 'edit', 0xE710: 'add',
    0xE711: 'dismiss', 0xE712: 'more_horizontal', 0xE713: 'settings', 0xE719: 'shopping_bag', 0xE71C: 'filter',
    0xE721: 'search', 0xE723: 'attach', 0xE72E: 'lock_closed', 0xE734: 'star', 0xE73E: 'checkmark',
    0xE749: 'print', 0xE74D: 'delete', 0xE76B: 'chevron_left', 0xE76C: 'chevron_right',
    0xE777: 'arrow_counterclockwise', 0xE783: 'error_circle', 0xE787: 'calendar_ltr', 0xE7BA: 'warning',
    0xE7BF: 'cart', 0xE7C1: 'flag', 0xE80F: 'home', 0xE823: 'clock', 0xE825: 'building_bank',
    0xE895: 'arrow_sync', 0xE896: 'arrow_download', 0xE8A5: 'document_text', 0xE8A9: 'grid', 0xE8AB: 'arrow_swap',
    0xE8AC: 'rename', 0xE8B5: 'arrow_import', 0xE8C7: 'payment', 0xE8C8: 'copy', 0xE8D7: 'key', 0xE8EC: 'tag',
    0xE8EF: 'calculator', 0xE8FD: 'receipt', 0xE9CE: 'question_circle', 0xE9D2: 'data_trending',
    0xEB05: 'data_pie', 0xEB9F: 'image', 0xEDA2: 'storage',
}

def build(src_dir, style, family):
    names = json.load(open(f'{src_dir}/FluentSystemIcons-{style}.json'))
    font = TTFont(f'{src_dir}/FluentSystemIcons-{style}.ttf')
    cmap = font.getBestCmap()
    chosen = {}
    for cp, name in ICONS.items():
        for size in (20, 24, 16, 28):
            key = f'ic_fluent_{name}_{size}_{style.lower()}'
            if key in names:
                chosen[cp] = cmap[names[key]]
                break
        else:
            sys.exit(f'missing icon: {name} ({style})')
    glyphs = sorted(set(chosen.values()))
    opts = subset.Options()
    opts.glyph_names = True
    opts.notdef_outline = True
    opts.name_IDs = ['*']
    opts.layout_features = []
    sub = subset.Subsetter(opts)
    sub.populate(glyphs=glyphs)
    sub.subset(font)
    # New cmap: our code points -> the Fluent glyphs
    from fontTools.ttLib.tables._c_m_a_p import cmap_format_4
    table = cmap_format_4(4)
    table.platformID, table.platEncID, table.language = 3, 1, 0
    table.cmap = chosen
    font['cmap'].tables = [table]
    for rec in font['name'].names:
        if rec.nameID in (1, 16):
            rec.string = family
        elif rec.nameID == 4:
            rec.string = family
        elif rec.nameID == 6:
            rec.string = family.replace(' ', '')
        elif rec.nameID in (2, 17):
            rec.string = 'Regular'
    out = f'src/CoinPilot.Desktop/Assets/Fonts/WillowWalletIcons-{style}.ttf'
    font.save(out)
    print(out, len(chosen), 'icons')

src = sys.argv[1]
build(src, 'Regular', 'WillowWallet Icons')
build(src, 'Filled', 'WillowWallet Icons Filled')
