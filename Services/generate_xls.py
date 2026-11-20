#!/usr/bin/env python3
import sys
import json
import argparse
import xlwt

def build_xls(input_json_path, output_xls_path):
    with open(input_json_path, 'r', encoding='utf-8') as f:
        data = json.load(f)

    sheet_name = data.get("sheetName", "Sheet1")[:31]
    headers = data.get("headers", [])
    rows = data.get("rows", [])

    wb = xlwt.Workbook(encoding='utf-8')
    ws = wb.add_sheet(sheet_name)

    # Styles
    header_font = xlwt.Font()
    header_font.name = 'Segoe UI'
    header_font.bold = True
    header_font.color_index = 0x08 # Black

    header_pattern = xlwt.Pattern()
    header_pattern.pattern = xlwt.Pattern.SOLID_PATTERN
    header_pattern.pattern_fore_colour = 0x1F # Light grey / silver (index 31)

    borders = xlwt.Borders()
    borders.left = xlwt.Borders.THIN
    borders.right = xlwt.Borders.THIN
    borders.top = xlwt.Borders.THIN
    borders.bottom = xlwt.Borders.THIN

    alignment = xlwt.Alignment()
    alignment.horz = xlwt.Alignment.HORZ_LEFT
    alignment.vert = xlwt.Alignment.VERT_CENTER

    header_style = xlwt.XFStyle()
    header_style.font = header_font
    header_style.pattern = header_pattern
    header_style.borders = borders
    header_style.alignment = alignment

    data_font = xlwt.Font()
    data_font.name = 'Segoe UI'
    data_font.bold = False

    data_style = xlwt.XFStyle()
    data_style.font = data_font
    data_style.borders = borders
    data_style.alignment = alignment

    col_widths = [len(str(h)) for h in headers]

    for col_idx, h in enumerate(headers):
        ws.write(0, col_idx, str(h), header_style)

    for row_idx, row in enumerate(rows, start=1):
        for col_idx, val in enumerate(row):
            str_val = "" if val is None else str(val)
            ws.write(row_idx, col_idx, str_val, data_style)
            if col_idx < len(col_widths):
                col_widths[col_idx] = max(col_widths[col_idx], len(str_val))

    for col_idx, width in enumerate(col_widths):
        # xlwt column width is 1/256th of character width, plus padding
        adjusted_width = min(max((width + 4) * 280, 2600), 14000)
        ws.col(col_idx).width = adjusted_width

    wb.save(output_xls_path)

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Generate genuine BIFF8 .xls file using xlwt")
    parser.add_argument("-i", "--input", required=True, help="Path to input JSON file")
    parser.add_argument("-o", "--output", required=True, help="Path to output XLS file")
    args = parser.parse_args()
    build_xls(args.input, args.output)
