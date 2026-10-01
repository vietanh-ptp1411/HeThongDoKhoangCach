# -*- coding: utf-8 -*-
"""
Sinh file CSV (định dạng "Write to CSV File" của GX Works2) cho chương trình PLC test
Q03UDV – mô phỏng luồng đo lực căng belt để thử kết nối MC Protocol với phần mềm.

    python Gen_PLC_Test_CSV.py

Kết quả (cùng thư mục):
    TEST_Q03UDV_MAIN.csv     – chương trình MAIN dạng list, đọc bằng Edit > Read from CSV File
    TEST_Q03UDV_COMMENT.csv  – chú thích thiết bị (tùy chọn)

File ghi giống file GX Works2 tự xuất: UTF-16 LE có BOM, phân cách TAB, mọi ô có dấu ngoặc kép.
Bảng địa chỉ khớp PlcSettings.ApplyMcDefaults() trong Models/AppSettings.cs.
"""
import sys
from pathlib import Path

OUT_DIR = Path(__file__).resolve().parent
PROGRAM_NAME = "MAIN"
PLC_TYPE = "QCPU (Q mode) Q03UDV"

# Số step cơ bản của lệnh trên QnU (để điền cột Step No.; GX Works2 tính lại khi Convert).
STEPS = {
    "LD": 1, "LDI": 1, "AND": 1, "ANI": 1, "OR": 1, "ORI": 1,
    "OUT": 1, "SET": 1, "RST": 1, "PLS": 2, "END": 1,
    "INC": 2, "INCP": 2, "DINC": 2,
    "MOV": 2, "DMOV": 3, "EMOV": 3, "FMOV": 4,
    "+": 4, "E+": 4, "E-": 4, "E*": 4, "FLT": 3,
    "LD<>": 3, "LD>=": 3, "LDE>=": 3, "ANDE<=": 3,
}

# Giá trị kết quả đo lần lượt cho mỗi lần START (mm) – vòng lại sau 10 lần hoặc khi RESET.
TEST_VALUES = ["3.7", "4.5", "3.2", "5.8", "4.1", "6.4", "2.9", "5.0", "3.55", "5.2"]

PROGRAM = [
    # ---- Khởi tạo khi chuyển sang RUN: bảng giá trị test, bộ đếm, khoảng cách nền
    ("LD", "SM402"),
    *[("EMOV", f"E{v}", f"D{200 + 2 * i}") for i, v in enumerate(TEST_VALUES)],
    ("FMOV", "K0", "D110", "K6"),
    ("EMOV", "E0.0", "D104"),
    ("MOV", "K0", "D130"),
    ("EMOV", "E3.7", "D150"),

    # ---- M100 loadcell ổn định: luôn ON
    ("LD", "SM400"),
    ("OUT", "M100"),

    # ---- M110 = đã có bit chủng loại (bất kỳ bit nào M40..M51)
    ("LD<>", "K3M40", "K0"),
    ("OUT", "M110"),

    # ---- Quy cách theo bit chủng loại: mặc định 3.0~4.0 (LCP M45 / phát sinh), PP1 M41 / M1-M2 M42 / NF M43: 4.0~5.0,
    #      GSM M40 / CPX M44: 3.5~6.0. D120 = LSL, D122 = USL (số thực)
    ("LD", "SM400"),
    ("EMOV", "E3.0", "D120"),
    ("EMOV", "E4.0", "D122"),
    ("LD", "M41"),
    ("OR", "M42"),
    ("OR", "M43"),
    ("EMOV", "E4.0", "D120"),
    ("EMOV", "E5.0", "D122"),
    ("LD", "M40"),
    ("OR", "M44"),
    ("EMOV", "E3.5", "D120"),
    ("EMOV", "E6.0", "D122"),

    # ---- START (M0): nhận lệnh khi đã có chủng loại và chưa chạy → M113 ON đúng 1 scan
    ("LD", "M0"),
    ("AND", "M110"),
    ("ANI", "M104"),
    ("OUT", "M113"),
    ("LD", "M113"),
    ("SET", "M104"),
    ("RST", "M101"),
    ("RST", "M102"),
    ("RST", "M103"),
    ("RST", "M111"),
    # START khi chưa có chủng loại → M111 báo bị từ chối
    ("LD", "M0"),
    ("ANI", "M110"),
    ("SET", "M111"),
    # Chọn giá trị đo: theo bảng D200.. (D130 = thứ tự) hoặc D140 khi bật M130 (nhập tay)
    ("LD", "M113"),
    ("ANI", "M130"),
    ("+", "D130", "D130", "Z0"),
    ("EMOV", "D200Z0", "D150"),
    ("LD", "M113"),
    ("AND", "M130"),
    ("EMOV", "D140", "D150"),
    # PLC tự xóa bit lệnh START
    ("LD", "M0"),
    ("RST", "M0"),

    # ---- Đang đo 1,5 giây (T0, 100 ms)
    ("LD", "M104"),
    ("OUT", "T0", "K15"),
    ("LD", "T0"),
    ("PLS", "M112"),

    # ---- Phân định: M114 = LSL <= giá trị <= USL
    ("LDE>=", "D150", "D120"),
    ("ANDE<=", "D150", "D122"),
    ("OUT", "M114"),

    # ---- Đo xong (M112): chốt kết quả D104, tăng bộ đếm, OK/NG, bật M101, dừng chạy
    ("LD", "M112"),
    ("EMOV", "D150", "D104"),
    ("DINC", "D110"),
    ("LD", "M112"),
    ("AND", "M114"),
    ("SET", "M102"),
    ("DINC", "D112"),
    ("LD", "M112"),
    ("ANI", "M114"),
    ("SET", "M103"),
    ("DINC", "D114"),
    ("LD", "M112"),
    ("ANI", "M130"),
    ("INC", "D130"),
    ("LD>=", "D130", "K10"),
    ("MOV", "K0", "D130"),
    ("LD", "M112"),
    ("SET", "M101"),
    ("RST", "M104"),

    # ---- Giữ bit Đo xong 1 giây (T1) rồi tự tắt
    ("LD", "M101"),
    ("OUT", "T1", "K10"),
    ("LD", "T1"),
    ("RST", "M101"),

    # ---- STOP (M15): dừng đo, PLC tự xóa bit lệnh
    ("LD", "M15"),
    ("RST", "M104"),
    ("RST", "M101"),
    ("RST", "M15"),

    # ---- RESET (M16): xóa bộ đếm, kết quả, OK/NG, quay lại giá trị test đầu tiên
    ("LD", "M16"),
    ("RST", "M104"),
    ("RST", "M101"),
    ("RST", "M102"),
    ("RST", "M103"),
    ("RST", "M111"),
    ("FMOV", "K0", "D110", "K6"),
    ("EMOV", "E0.0", "D104"),
    ("MOV", "K0", "D130"),
    ("EMOV", "E3.7", "D150"),
    ("RST", "M16"),

    # ---- Giá trị chạy liên tục: D300 răng cưa 0..19 (mỗi 0,1 s)
    #      loadcell D100 = 2.0 ± 0.3 gf, khoảng cách D102 = D150 ± 0.05 mm
    ("LD", "SM410"),
    ("INCP", "D300"),
    ("LD>=", "D300", "K20"),
    ("MOV", "K0", "D300"),
    ("LD", "SM400"),
    ("FLT", "D300", "D302"),
    ("E-", "D302", "E10.0", "D304"),
    ("E*", "D304", "E0.005", "D306"),
    ("E*", "D304", "E0.03", "D308"),
    ("E+", "E2.0", "D308", "D100"),
    ("E+", "D150", "D306", "D102"),

    ("END",),
]

COMMENTS = [
    ("M0", "START from PC (PLC clears)"),
    ("M15", "STOP from PC (PLC clears)"),
    ("M16", "RESET from PC (PLC clears)"),
    ("M40", "Model GSM"),
    ("M41", "Model PP1"),
    ("M42", "Model M1-M2"),
    ("M43", "Model NF83"),
    ("M44", "Model CPX"),
    ("M45", "Model LCP"),
    ("M100", "Loadcell stable"),
    ("M101", "Measure done (hold 1s)"),
    ("M102", "Judge OK"),
    ("M103", "Judge NG"),
    ("M104", "Running / measuring"),
    ("M110", "Model bit selected"),
    ("M111", "START rejected: no model"),
    ("M112", "Measure complete pulse"),
    ("M113", "START accepted pulse"),
    ("M114", "Value in spec"),
    ("M130", "Manual value mode (D140)"),
    ("T0", "Measure time 1.5s"),
    ("T1", "Done hold 1s"),
    ("D100", "Loadcell gf (float)"),
    ("D102", "Distance mm (float)"),
    ("D104", "Result mm (float)"),
    ("D110", "TOTAL (32bit)"),
    ("D112", "OK count (32bit)"),
    ("D114", "NG count (32bit)"),
    ("D120", "LSL mm (float)"),
    ("D122", "USL mm (float)"),
    ("D130", "Test table index 0-9"),
    ("D140", "Manual value mm (float)"),
    ("D150", "Target value mm (float)"),
    ("D200", "Test table 10 floats"),
    ("D300", "Wobble counter 0-19"),
]


def row(*cells):
    return "\t".join(f'"{c}"' for c in cells)


def build_program():
    lines = [row(PROGRAM_NAME), row("PLC Information:", PLC_TYPE),
             row("Step No.", "Line Statement", "Instruction", "I/O(Device)", "Blank", "PI Statement", "Note")]
    step = 0
    for instr, *operands in PROGRAM:
        first = operands[0] if operands else ""
        lines.append(row(step, "", instr, first, "", "", ""))
        for op in operands[1:]:
            lines.append(row("", "", "", op, "", "", ""))
        step += STEPS[instr]
    return lines


def build_comments():
    return [row("COMMENT"), row("Device Name", "Comment"), *(row(d, c) for d, c in COMMENTS)]


def write(name, lines):
    path = OUT_DIR / name
    path.write_text("\r\n".join(lines) + "\r\n", encoding="utf-16", newline="")
    print(f"Đã ghi {path} ({len(lines)} dòng)")


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    write("TEST_Q03UDV_MAIN.csv", build_program())
    write("TEST_Q03UDV_COMMENT.csv", build_comments())
