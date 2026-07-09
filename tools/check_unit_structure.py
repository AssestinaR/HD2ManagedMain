import os
import struct
import sys
import tempfile
import zipfile
from dataclasses import dataclass
from pathlib import Path

REPATCHER_ROOT = Path(__file__).resolve().parents[1] / 'docs' / 'hd2-repatcher-main'
sys.path.insert(0, str(REPATCHER_ROOT))
from slim import close_file_handles, get_package_toc, get_resource_from_package, is_slim_version, slim_init

UNIT_TYPE_ID = 16187218042980615487
MAGIC = 4026531857
ENTRY_SIZE = 80
TYPE_SIZE = 32

@dataclass
class TocEntry:
    file_id: int
    type_id: int
    toc_data_offset: int
    stream_file_offset: int
    gpu_resource_offset: int
    unknown1: int
    unknown2: int
    toc_data_size: int
    stream_size: int
    gpu_resource_size: int
    unknown3: int
    unknown4: int
    entry_index: int


def read_u32(data, offset):
    return struct.unpack_from('<I', data, offset)[0]


def read_toc_entries(toc_path: Path):
    data = toc_path.read_bytes()
    if len(data) < 72:
        raise ValueError(f'TOC too small: {toc_path}')
    magic, num_types, num_files = struct.unpack_from('<III', data, 0)
    if magic != MAGIC:
        raise ValueError(f'Invalid TOC magic {magic}: {toc_path}')

    def score(header_size):
        entries_offset = header_size + num_types * TYPE_SIZE
        if entries_offset + num_files * ENTRY_SIZE > len(data):
            return -10**9
        type_ids = set()
        declared = 0
        for i in range(num_types):
            off = header_size + i * TYPE_SIZE
            if off + TYPE_SIZE > len(data):
                return -10**9
            type_ids.add(struct.unpack_from('<Q', data, off + 8)[0])
            declared += struct.unpack_from('<Q', data, off + 16)[0]
        s = 1000 if declared == num_files else 0
        for i in range(num_files):
            off = entries_offset + i * ENTRY_SIZE
            file_id, type_id = struct.unpack_from('<QQ', data, off)
            if file_id:
                s += 1
            if type_id in type_ids:
                s += 10
        return s

    header_size = 72 if score(72) > score(60) else 60
    entries_offset = header_size + num_types * TYPE_SIZE
    entries = []
    for i in range(num_files):
        off = entries_offset + i * ENTRY_SIZE
        fields = struct.unpack_from('<QQQQQQQIIIIII', data, off)
        entries.append(TocEntry(*fields))
    return entries, header_size, num_types, num_files


def unit_summary(unit_data: bytes):
    if len(unit_data) < 0x38:
        return {'valid': False, 'reason': 'unit data shorter than 0x38', 'size': len(unit_data)}
    version = read_u32(unit_data, 0x2C)
    lod_group_offset = read_u32(unit_data, 0x30)
    joint_list_offset = read_u32(unit_data, 0x34)
    lod_group_size = joint_list_offset - lod_group_offset if joint_list_offset >= lod_group_offset else -1
    valid_offsets = 0 <= lod_group_offset <= joint_list_offset <= len(unit_data)
    return {
        'valid': valid_offsets,
        'size': len(unit_data),
        'version': version,
        'version_hex': f'0x{version:08X}',
        'lod_group_offset': lod_group_offset,
        'joint_list_offset': joint_list_offset,
        'lod_group_size': lod_group_size,
        'old_layout_threshold': version < 0xA4CD36,
        'reason': None if valid_offsets else 'invalid lod/joint offsets',
    }


def compare_to_game(mod_unit, game_unit):
    if game_unit is None:
        return 'missing_in_game'
    if not mod_unit.get('valid'):
        return 'invalid_mod_unit'
    if not game_unit.get('valid'):
        return 'invalid_game_unit'
    issues = []
    if mod_unit.get('version') != game_unit.get('version'):
        issues.append('version')
    if mod_unit.get('lod_group_size') != game_unit.get('lod_group_size'):
        issues.append('lod_size')
    if mod_unit.get('old_layout_threshold'):
        issues.append('old_layout')
    return 'ok' if not issues else '+'.join(issues)


def find_patch_sets(root: Path):
    tocs = []
    for p in root.rglob('*'):
        if not p.is_file():
            continue
        name = p.name.lower()
        if '.patch_' in name and not name.endswith('.stream') and not name.endswith('.gpu_resources'):
            tocs.append(p)
    return sorted(tocs)


def extract_if_zip(path: Path):
    if path.suffix.lower() != '.zip':
        return path, None
    temp = Path(tempfile.mkdtemp(prefix='hd2mod_unit_check_'))
    with zipfile.ZipFile(path, 'r') as z:
        z.extractall(temp)
    return temp, temp


def analyze(label: str, input_path: Path):
    scan_root, temp = extract_if_zip(input_path)
    try:
        tocs = find_patch_sets(scan_root if scan_root.is_dir() else scan_root.parent)
        if scan_root.is_file():
            tocs = [scan_root]
        result = {
            'label': label,
            'input': str(input_path),
            'toc_count': len(tocs),
            'unit_count': 0,
            'versions': {},
            'lod_sizes': {},
            'invalid_units': [],
            'units': [],
        }
        for toc in tocs:
            entries, header_size, num_types, num_files = read_toc_entries(toc)
            patch_root = toc.parent
            unit_entries = [e for e in entries if e.type_id == UNIT_TYPE_ID]
            for e in unit_entries:
                data = toc.read_bytes()[e.toc_data_offset:e.toc_data_offset + e.toc_data_size]
                summary = unit_summary(data)
                summary.update({
                    'file_id': e.file_id,
                    'file_id_hex': f'0x{e.file_id:016X}',
                    'toc': str(toc),
                    'toc_data_offset': e.toc_data_offset,
                    'toc_data_size': e.toc_data_size,
                })
                result['unit_count'] += 1
                result['units'].append(summary)
                if 'version_hex' in summary:
                    result['versions'][summary['version_hex']] = result['versions'].get(summary['version_hex'], 0) + 1
                    result['lod_sizes'][str(summary['lod_group_size'])] = result['lod_sizes'].get(str(summary['lod_group_size']), 0) + 1
                if not summary.get('valid'):
                    result['invalid_units'].append(summary)
        return result
    finally:
        pass


def load_game_unit_index(game_data_path: Path, wanted_file_ids):
    slim_init(str(game_data_path))
    if not is_slim_version():
        raise RuntimeError('This temporary path expects slim game data.')

    wanted = set(wanted_file_ids)
    mapping = {}
    bundle_database = game_data_path / 'bundle_database.data'
    data = bundle_database.read_bytes()
    num_packages = int.from_bytes(data[4:8], 'little')
    for i in range(num_packages):
        offset = 0x10 + 0x33 * i
        package_name = data[offset:offset + 0x33].decode(errors='ignore').split('\x17')[0]
        if not package_name:
            continue
        toc_data = get_package_toc(package_name)
        if not toc_data:
            continue
        try:
            entries, _, _, _ = read_toc_entries_from_bytes(toc_data)
        except Exception:
            continue
        for entry in entries:
            if entry.type_id == UNIT_TYPE_ID and entry.file_id in wanted:
                mapping[entry.file_id] = (package_name, entry.toc_data_offset, entry.toc_data_size)
        if wanted.issubset(mapping.keys()):
            break
    return mapping


def read_toc_entries_from_bytes(data: bytes):
    if len(data) < 72:
        raise ValueError('TOC too small')
    magic, num_types, num_files = struct.unpack_from('<III', data, 0)
    if magic != MAGIC:
        raise ValueError(f'Invalid TOC magic {magic}')

    def score(header_size):
        entries_offset = header_size + num_types * TYPE_SIZE
        if entries_offset + num_files * ENTRY_SIZE > len(data):
            return -10**9
        type_ids = set()
        declared = 0
        for i in range(num_types):
            off = header_size + i * TYPE_SIZE
            if off + TYPE_SIZE > len(data):
                return -10**9
            type_ids.add(struct.unpack_from('<Q', data, off + 8)[0])
            declared += struct.unpack_from('<Q', data, off + 16)[0]
        s = 1000 if declared == num_files else 0
        for i in range(num_files):
            off = entries_offset + i * ENTRY_SIZE
            file_id, type_id = struct.unpack_from('<QQ', data, off)
            if file_id:
                s += 1
            if type_id in type_ids:
                s += 10
        return s

    header_size = 72 if score(72) > score(60) else 60
    entries_offset = header_size + num_types * TYPE_SIZE
    entries = []
    for i in range(num_files):
        off = entries_offset + i * ENTRY_SIZE
        fields = struct.unpack_from('<QQQQQQQIIIIII', data, off)
        entries.append(TocEntry(*fields))
    return entries, header_size, num_types, num_files


def attach_game_comparison(reports, game_data_path: Path):
    wanted = {u['file_id'] for r in reports for u in r['units']}
    game_index = load_game_unit_index(game_data_path, wanted)
    game_summaries = {}
    for file_id, (package_name, offset, size) in game_index.items():
        data = get_resource_from_package(package_name, offset, size)
        summary = unit_summary(data)
        summary.update({
            'file_id': file_id,
            'file_id_hex': f'0x{file_id:016X}',
            'package': package_name,
            'toc_data_offset': offset,
            'toc_data_size': size,
        })
        game_summaries[file_id] = summary

    for r in reports:
        status_counts = {}
        for unit in r['units']:
            game_unit = game_summaries.get(unit['file_id'])
            status = compare_to_game(unit, game_unit)
            unit['game_status'] = status
            unit['game_version_hex'] = None if game_unit is None else game_unit.get('version_hex')
            unit['game_lod_group_size'] = None if game_unit is None else game_unit.get('lod_group_size')
            unit['game_size'] = None if game_unit is None else game_unit.get('size')
            status_counts[status] = status_counts.get(status, 0) + 1
        r['game_status_counts'] = status_counts
    close_file_handles()


def print_report(reports):
    for r in reports:
        print(f"\n=== {r['label']} ===")
        print(f"input: {r['input']}")
        print(f"toc files: {r['toc_count']}")
        print(f"unit entries: {r['unit_count']}")
        print(f"versions: {r['versions']}")
        print(f"lod group sizes: {r['lod_sizes']}")
        print(f"invalid units: {len(r['invalid_units'])}")
        if 'game_status_counts' in r:
            print(f"game comparison: {r['game_status_counts']}")
        for u in r['units'][:20]:
            print(
                f"unit {u['file_id_hex']} version={u.get('version_hex')} "
                f"size={u.get('size')} lod={u.get('lod_group_size')} "
                f"old_layout={u.get('old_layout_threshold')} valid={u.get('valid')} reason={u.get('reason')} "
                f"game={u.get('game_status')} game_version={u.get('game_version_hex')} game_lod={u.get('game_lod_group_size')}"
            )

    if len(reports) == 2:
        a, b = reports
        a_units = {u['file_id']: u for u in a['units']}
        b_units = {u['file_id']: u for u in b['units']}
        common = sorted(set(a_units) & set(b_units))
        print('\n=== pair comparison ===')
        print(f"common unit ids: {len(common)}")
        print(f"only {a['label']}: {len(set(a_units) - set(b_units))}")
        print(f"only {b['label']}: {len(set(b_units) - set(a_units))}")
        for fid in common[:30]:
            ua = a_units[fid]
            ub = b_units[fid]
            print(
                f"{fid:016X}: {a['label']} version={ua.get('version_hex')} lod={ua.get('lod_group_size')} size={ua.get('size')} | "
                f"{b['label']} version={ub.get('version_hex')} lod={ub.get('lod_group_size')} size={ub.get('size')}"
            )


def print_batch_report(reports):
    print('label\tunits\tinvalid\tversions\tgame comparison')
    for r in reports:
        versions = ','.join(f'{k}:{v}' for k, v in sorted(r['versions'].items())) or '-'
        game = ','.join(f'{k}:{v}' for k, v in sorted(r.get('game_status_counts', {}).items())) or '-'
        print(f"{r['label']}\t{r['unit_count']}\t{len(r['invalid_units'])}\t{versions}\t{game}")


def resolve_batch_inputs(root: Path):
    if not root.is_dir():
        return [root]
    children = [p for p in root.iterdir() if p.is_dir()]
    return sorted(children) if children else [root]


def main():
    if len(sys.argv) < 3:
        print('usage: check_unit_structure.py [--game-data <game_data_dir>] [--batch-root <mods_dir>] [--summary] <mod_zip_or_dir> [<mod_zip_or_dir>...]')
        return 2
    args = sys.argv[1:]
    game_data_path = None
    summary = False
    batch_root = None
    if args[:1] == ['--game-data']:
        game_data_path = Path(args[1])
        args = args[2:]
    if args[:1] == ['--batch-root']:
        batch_root = Path(args[1])
        args = args[2:]
    if args[:1] == ['--summary']:
        summary = True
        args = args[1:]
    inputs = resolve_batch_inputs(batch_root) if batch_root is not None else [Path(path) for path in args]
    reports = [analyze(path.name, path) for path in inputs]
    if game_data_path is not None:
        attach_game_comparison(reports, game_data_path)
    if summary or batch_root is not None:
        print_batch_report(reports)
    else:
        print_report(reports)
    return 0

if __name__ == '__main__':
    raise SystemExit(main())
