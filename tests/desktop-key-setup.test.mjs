import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { createPrivateKey } from 'node:crypto';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve, sep } from 'node:path';
import test from 'node:test';
import { parseEnv } from 'node:util';

const script = resolve('scripts/setup-desktop-keys.mjs');
function fixture(text, check) {
  const folder = mkdtempSync(join(tmpdir(), 'optimizer-signing-setup-'));
  const file = join(folder, '.env.development');
  try {
    writeFileSync(file, text);
    check(file, () =>
      spawnSync(process.execPath, [script], { cwd: folder, encoding: 'utf8' })
    );
  } finally {
    assert.ok(
      resolve(folder).startsWith(
        resolve(tmpdir()) + sep + 'optimizer-signing-setup-'
      )
    );
    rmSync(folder, { recursive: true, force: true });
  }
}

test('key setup preserves an appended persistent key across repeated runs', () => {
  fixture(
    'OPTIMIZER_LICENSE_PRIVATE_KEY=\nOPTIMIZER_LICENSE_KEY_ID=\n',
    (file, run) => {
      const first = run();
      assert.equal(first.status, 0);
      const original = readFileSync(file, 'utf8');
      const saved = parseEnv(original);
      const key = createPrivateKey({
        key: Buffer.from(saved.OPTIMIZER_LICENSE_PRIVATE_KEY, 'base64url'),
        format: 'der',
        type: 'pkcs8',
      });
      assert.equal(key.asymmetricKeyType, 'rsa');
      assert.equal(key.asymmetricKeyDetails.modulusLength, 3072);
      const second = run();
      assert.equal(second.status, 0);
      assert.equal(readFileSync(file, 'utf8'), original);
      assert.match(second.stdout, /preserved/);
      assert.ok(
        !(first.stdout + first.stderr + second.stdout + second.stderr).includes(
          saved.OPTIMIZER_LICENSE_PRIVATE_KEY
        )
      );
    }
  );
});

test('quoted blank template entries are initialized once', () => {
  fixture(
    'OPTIMIZER_LICENSE_PRIVATE_KEY=""\nOPTIMIZER_LICENSE_KEY_ID=\'\'\n',
    (file, run) => {
      assert.equal(run().status, 0);
      const original = readFileSync(file, 'utf8');
      assert.equal(run().status, 0);
      assert.equal(readFileSync(file, 'utf8'), original);
    }
  );
});

test('incomplete or invalid existing keys are never overwritten', () => {
  for (const text of [
    'OPTIMIZER_LICENSE_KEY_ID=existing\n',
    'OPTIMIZER_LICENSE_PRIVATE_KEY=invalid\n',
    'OPTIMIZER_LICENSE_KEY_ID=existing\nOPTIMIZER_LICENSE_PRIVATE_KEY=invalid\n',
  ]) {
    fixture(text, (file, run) => {
      assert.notEqual(run().status, 0);
      assert.equal(readFileSync(file, 'utf8'), text);
    });
  }
});
