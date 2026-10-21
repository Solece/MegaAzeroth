"""Read user-owned Azeroth MPQ archives. Never writes to the source folder."""
from pathlib import Path
import mpyq


class Archives:
    def __init__(self, data_dir):
        self.data_dir = Path(data_dir).resolve(strict=True)
        names = ['patch.MPQ'] + [f'patch-{i}.MPQ' for i in range(2, 8)]
        for name in names:
            if not (self.data_dir / name).is_file():
                raise FileNotFoundError(self.data_dir / name)
        self.archives = [(name, mpyq.MPQArchive(str(self.data_dir / name), listfile=False)) for name in names]

    def read(self, path):
        for name, archive in reversed(self.archives):
            entry = archive.get_hash_table_entry(path)
            if entry is None:
                continue
            block = archive.block_table[entry.block_table_index]
            if not block.flags & 0x80000000:
                continue
            if not block.flags & (0x200 | 0x100 | 0x10000):
                archive.file.seek(archive.header['offset'] + block.offset)
                data = archive.file.read(block.size)
            else:
                data = archive.read_file(path)
            return data, name
        raise FileNotFoundError(path)

    def __enter__(self):
        return self

    def __exit__(self, *_):
        for _, archive in self.archives:
            archive.file.close()
