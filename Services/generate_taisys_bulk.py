#!/usr/bin/env python3
import sys
import json
import argparse
import openpyxl
from openpyxl.worksheet.datavalidation import DataValidation

HEADERS = [
    "ACTION", "ICCID", "PO Number", "device_IMEI", "device_ESN",
    "Device_make", "Device_model", "engine_number", "Registration Number",
    "Single/Dual_status", "PlanName", "Validity", "Project", "RTO State",
    "Device_type", "custodianName", "custodianMobNumber", "poi_type",
    "poi_number", "poa_type", "poa_number", "address_proof_path",
    "id_proof_S3_path", "pan_S3_path", "address", "City", "PinCode",
    "IP1", "IP2", "IP3", "IP4", "Mob1", "Mob2", "Mob3", "Mob4", "Profile"
]

STATIC_ROW = {
    "ACTION": "OP",
    "PO Number": None,
    "device_ESN": "7262330598",
    "Device_make": "Markon",
    "Device_model": "Markon",
    "engine_number": None,
    "Registration Number": None,
    "Single/Dual_status": "D",
    "PlanName": "250MB_100SMS",
    "Validity": "12.25",
    "Project": "AIS140",
    "RTO State": "Delhi",
    "Device_type": "VTS",
    "custodianName": "Test",
    "custodianMobNumber": "7011440510",
    "poi_type": None,
    "poi_number": None,
    "poa_type": None,
    "poa_number": None,
    "address_proof_path": None,
    "id_proof_S3_path": None,
    "pan_S3_path": None,
    "address": "Delhi",
    "City": "Delhi",
    "PinCode": "110020",
    "IP1": "164.100.64.227",
    "IP2": "fota.markongps.in",
    "IP3": "markongps.in",
    "IP4": "py-ais140.bloomcomputers.com",
    "Mob1": "9035163862",
    "Mob2": "9448579928",
    "Mob3": "9758458458",
    "Mob4": "9563958958",
    "Profile": "A+B"
}

def generate_taisys_workbook(items):
    wb = openpyxl.Workbook()
    # Primary sheet
    ws = wb.active
    ws.title = "activation-sheet"
    
    # Empty Sheet1 as per template specification
    wb.create_sheet(title="Sheet1")

    # Write headers
    for col_idx, header in enumerate(HEADERS, start=1):
        cell = ws.cell(row=1, column=col_idx, value=header)
        cell.number_format = '@'

    # If no items provided, generate 20 sample rows (rows 2 to 21)
    if not items:
        items = [{"iccid": None, "imei": None} for _ in range(20)]

    for row_offset, item in enumerate(items):
        row_idx = row_offset + 2
        iccid = item.get("iccid") or item.get("Iccid") or None
        if iccid is not None:
            iccid = str(iccid).strip()
            if not iccid:
                iccid = None

        imei = item.get("imei") or item.get("Imei") or None
        if imei is not None:
            imei = str(imei).strip()
            if not imei:
                imei = None

        for col_idx, col in enumerate(HEADERS, start=1):
            cell = ws.cell(row=row_idx, column=col_idx)
            val = None
            if col == "ICCID":
                val = iccid
            elif col == "device_IMEI":
                val = imei
            else:
                val = STATIC_ROW.get(col, None)

            if val is not None and val != "":
                cell.value = str(val)
                cell.number_format = '@'
            else:
                cell.value = None

    max_row = max(len(items) + 1, 21)

    # Recreate the exact Data Validations (dropdowns) on columns K, L, and AJ
    # Column K (PlanName): Dropdown list -> "200MB_100SMS,250MB_100SMS"
    dv_plan = DataValidation(type="list", formula1='"200MB_100SMS,250MB_100SMS"', allow_blank=True)
    ws.add_data_validation(dv_plan)
    dv_plan.add(f"K2:K{max_row}")

    # Column L (Validity): Dropdown list -> "24.25,12.25"
    dv_validity = DataValidation(type="list", formula1='"24.25,12.25"', allow_blank=True)
    ws.add_data_validation(dv_validity)
    dv_validity.add(f"L2:L{max_row}")

    # Column AJ (Profile): Dropdown list -> "A+B,V+B"
    dv_profile = DataValidation(type="list", formula1='"A+B,V+B"', allow_blank=True)
    ws.add_data_validation(dv_profile)
    dv_profile.add(f"AJ2:AJ{max_row}")

    return wb

def main():
    parser = argparse.ArgumentParser(description="Generate commercial-taisysbulkformat Excel")
    parser.add_argument("--input", "-i", help="Path to input JSON file containing array of {iccid, imei}")
    parser.add_argument("--json", "-j", help="Inline JSON string containing array of {iccid, imei}")
    parser.add_argument("--output", "-o", required=True, help="Path to output .xlsx file")
    args = parser.parse_args()

    items = []
    if args.input:
        with open(args.input, "r", encoding="utf-8") as f:
            items = json.load(f)
    elif args.json:
        try:
            items = json.loads(args.json)
        except Exception as e:
            print("Error parsing inline json:", e, file=sys.stderr)

    wb = generate_taisys_workbook(items)
    wb.save(args.output)
    print(f"Generated {args.output} successfully with {len(items)} rows.")

if __name__ == "__main__":
    main()
