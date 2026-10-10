import assert from 'node:assert/strict';
import test from 'node:test';

import {
  serveOptimizerDownload,
  type ReleaseBucket,
} from '../src/lib/optimizer-download';

const release = {
  version: '0.5.0',
  downloadUrl: 'https://product.example/api/optimizer/download',
  sha256: 'a'.repeat(64),
  sizeBytes: 4,
};
const url = `${release.downloadUrl}?sha256=${release.sha256}`;

test('download exposes only the current release hash and never arbitrary bucket keys', async () => {
  let reads = 0;
  const bucket: ReleaseBucket = {
    get: async () => {
      reads++;
      return null;
    },
    head: async () => {
      reads++;
      return null;
    },
  };
  for (const query of ['', '?sha256=other&key=private', '?key=../../secret']) {
    const response = await serveOptimizerDownload(
      new Request(release.downloadUrl + query),
      release,
      bucket
    );
    assert.equal(response.status, 404);
  }
  assert.equal(reads, 0);
  assert.equal(
    (await serveOptimizerDownload(new Request(url), null, bucket)).status,
    503
  );
  assert.equal(
    (await serveOptimizerDownload(new Request(url), release, undefined)).status,
    503
  );
});

test('download streams the verified artifact and HEAD does not read its body', async () => {
  let bodyReads = 0;
  const bucket: ReleaseBucket = {
    get: async (key) => {
      assert.equal(
        key,
        `0.5.0/${release.sha256}/optimizer-0.5.0-win-x64-preview.zip`
      );
      bodyReads++;
      return { size: 4, body: new Response('test').body! };
    },
    head: async () => ({ size: 4 }),
  };
  const response = await serveOptimizerDownload(
    new Request(url),
    release,
    bucket
  );
  assert.equal(response.status, 200);
  assert.equal(await response.text(), 'test');
  assert.equal(response.headers.get('content-length'), '4');
  assert.equal(response.headers.get('content-type'), 'application/zip');
  assert.match(response.headers.get('content-disposition')!, /attachment/);
  const head = await serveOptimizerDownload(
    new Request(url, { method: 'HEAD' }),
    release,
    bucket
  );
  assert.equal(head.status, 200);
  assert.equal(await head.text(), '');
  assert.equal(bodyReads, 1);
});

test('download fails closed when the artifact is missing or has the wrong size', async () => {
  let cancelled = false;
  const bucket: ReleaseBucket = {
    get: async () => ({
      size: 3,
      body: new ReadableStream({
        cancel() {
          cancelled = true;
        },
      }),
    }),
    head: async () => null,
  };
  assert.equal(
    (await serveOptimizerDownload(new Request(url), release, bucket)).status,
    503
  );
  assert.equal(cancelled, true);
  assert.equal(
    (
      await serveOptimizerDownload(
        new Request(url, { method: 'HEAD' }),
        release,
        bucket
      )
    ).status,
    503
  );
});
