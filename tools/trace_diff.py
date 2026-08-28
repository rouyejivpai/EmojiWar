#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
确定性打点 diff 工具（文档 §4）
输入两份 trace 二进制文件（两端各跑一遍生成），输出第一个有效分歧。

格式（与 DeterminismTracer 对应）：
  [CheckID:int][数据长度:short][数据:bytes][逻辑帧号:int]
整型数据：4 字节大端（C# BinaryWriter 为小端，此处按小端读）

用法：
  python3 trace_diff.py host_trace.bin client_trace.bin
"""
import struct
import sys

CHECK_NAMES = {
    1: "FrameStart",
    2: "PlayerSpawn",
    3: "EnemySpawn",
    4: "BulletSpawn",
    5: "RandomCall",
    6: "PlayerHp",
    7: "EnemyDeath",
    8: "WaveChange",
    9: "ShopOffer",
    10: "BattleEnd",
}


def read_records(path):
    """读取 trace 文件，返回 [(check_id, data_bytes, frame), ...]"""
    records = []
    with open(path, "rb") as f:
        while True:
            head = f.read(6)  # checkId(4) + length(2)
            if len(head) < 6:
                break
            check_id, length = struct.unpack("<ih", head)
            data = f.read(length)
            if len(data) < length:
                break  # 截断
            frame_bytes = f.read(4)
            if len(frame_bytes) < 4:
                break
            frame = struct.unpack("<i", frame_bytes)[0]
            records.append((check_id, data, frame))
    return records


def int_data(data):
    """按小端解析数据为 int 列表（整型检查点）。"""
    vals = []
    for i in range(0, len(data) - 3, 4):
        vals.append(struct.unpack("<i", data[i:i+4])[0])
    return vals


def format_data(check_id, data):
    name = CHECK_NAMES.get(check_id, "Check%d" % check_id)
    vals = int_data(data)
    return "%s(%s)" % (name, ", ".join(str(v) for v in vals))


def diff(path_a, path_b):
    recs_a = read_records(path_a)
    recs_b = read_records(path_b)
    print("文件 A: %s 共 %d 条检查点" % (path_a, len(recs_a)))
    print("文件 B: %s 共 %d 条检查点" % (path_b, len(recs_b)))

    n = min(len(recs_a), len(recs_b))
    for i in range(n):
        a = recs_a[i]
        b = recs_b[i]
        if a != b:
            print("\n===== 第一个有效分歧 @ 记录 #%d =====" % i)
            print("  A: %s (frame=%d)" % (format_data(a[0], a[1]), a[2]))
            print("  B: %s (frame=%d)" % (format_data(b[0], b[1]), b[2]))
            print("\n这是「状态注入点」（根因候选）：该检查点之后状态开始不同。")
            return 1

    if len(recs_a) != len(recs_b):
        print("\n===== 检查点数量不同 =====")
        print("  A 多出: %d 条" % (len(recs_a) - n))
        print("  B 多出: %d 条" % (len(recs_b) - n))
        return 1

    print("\n===== 两份 trace 完全一致 ✅ =====")
    return 0


if __name__ == "__main__":
    if len(sys.argv) < 3:
        print("用法: python3 trace_diff.py <trace_a.bin> <trace_b.bin>")
        sys.exit(2)
    sys.exit(diff(sys.argv[1], sys.argv[2]))
