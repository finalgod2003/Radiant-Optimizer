import assert from 'node:assert/strict';
import test from 'node:test';
import { z } from 'zod';

import { readJson, requireSameOrigin } from '../src/routes/api/desktop/-http';
import {
  createLicenseRateLimiter,
  licenseRequesterIdentity,
} from '../src/routes/api/desktop/-limits';

test('desktop request limits ignore cookies and untrusted forwarding headers', () => {
  const first = new Request('http://localhost:3010/api/desktop/start', {
    headers: {
      Cookie: 'a=1',
      'X-Forwarded-For': '192.0.2.1',
      'CF-Connecting-IP': '192.0.2.2',
    },
  });
  const rotated = new Request(first.url, {
    headers: {
      Cookie: 'a=changed',
      'X-Forwarded-For': '198.51.100.1',
      'CF-Connecting-IP': '198.51.100.2',
    },
  });
  assert.equal(
    licenseRequesterIdentity(first, '127.0.0.1'),
    licenseRequesterIdentity(rotated, '127.0.0.1')
  );
  assert.equal(
    licenseRequesterIdentity(first),
    licenseRequesterIdentity(rotated)
  );
  assert.equal(licenseRequesterIdentity(first, undefined, true), '192.0.2.2');
});

test('desktop rate-limit storage is bounded and expired entries are reclaimed', () => {
  let now = 10000;
  const limiter = createLicenseRateLimiter(3, () => now);
  assert.equal(limiter.accept('peer-a', 2000), true);
  assert.equal(limiter.accept('peer-a', 2000), false);
  assert.equal(limiter.accept('peer-b', 2000), true);
  assert.equal(limiter.accept('peer-c', 2000), true);
  for (let i = 0; i < 100; i++)
    assert.equal(limiter.accept(`extra-${i}`, 2000), false);
  assert.equal(limiter.size, 3);
  assert.equal(limiter.accept('peer-a', 2000), false);
  now += 2000;
  assert.equal(limiter.accept('peer-d', 2000), true);
  assert.equal(limiter.size, 1);
});

test('JSON limit counts streamed bytes, cancels early and ignores a false Content-Length', async () => {
  let pulled = 0;
  let canceled = false;
  const stream = new ReadableStream<Uint8Array>({
    pull(controller) {
      pulled++;
      if (pulled > 100) controller.close();
      else controller.enqueue(new Uint8Array(1024).fill(65));
    },
    cancel() {
      canceled = true;
    },
  });
  const request = new Request('http://localhost:3010/api/desktop/start', {
    method: 'POST',
    headers: { 'content-type': 'application/json', 'content-length': '1' },
    body: stream,
    duplex: 'half',
  } as RequestInit & { duplex: 'half' });
  await assert.rejects(readJson(request, z.object({})), { status: 413 });
  assert.equal(canceled, true);
  assert.ok(
    pulled <= 10,
    'the server must stop reading when the byte budget is exceeded'
  );
  const unicodeBody = JSON.stringify({ message: '中'.repeat(3000) });
  assert.ok(unicodeBody.length < 8192);
  await assert.rejects(
    readJson(
      new Request(request.url, {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: unicodeBody,
      }),
      z.object({ message: z.string() })
    ),
    { status: 413 }
  );
  assert.deepEqual(
    await readJson(
      new Request(request.url, {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: '{"message":"正常"}',
      }),
      z.object({ message: z.string() })
    ),
    { message: '正常' }
  );
});

test('web mutations require an explicit matching origin', () => {
  assert.throws(
    () => requireSameOrigin(new Request('http://localhost:3010/api/licenses')),
    { status: 403 }
  );
  assert.throws(
    () =>
      requireSameOrigin(
        new Request('http://localhost:3010/api/licenses', {
          headers: { Origin: 'https://untrusted.example' },
        })
      ),
    { status: 403 }
  );
  assert.doesNotThrow(() =>
    requireSameOrigin(
      new Request('http://localhost:3010/api/licenses', {
        headers: { Origin: 'http://localhost:3010' },
      })
    )
  );
});
