import '@testing-library/jest-dom/vitest';

// jsdom's Blob implements only the constructor surface used by DOM code and is
// missing the standard async readers (.text / .arrayBuffer) that browsers and
// Node provide. Polyfill them so APIs under test behave as they do for real.
type BlobWithReaders = Blob & { text(): Promise<string>; arrayBuffer(): Promise<ArrayBuffer> };
const proto = Blob.prototype as BlobWithReaders;

if (typeof proto.text !== 'function') {
  const read = (blob: Blob, as: 'text' | 'arrayBuffer') =>
    new Promise<never>((resolve, reject) => {
      const reader = new FileReader();
      reader.onload = () => resolve(reader.result as never);
      reader.onerror = () => reject(reader.error);
      if (as === 'text') reader.readAsText(blob);
      else reader.readAsArrayBuffer(blob);
    });

  proto.text = function (this: Blob) {
    return read(this, 'text') as unknown as Promise<string>;
  };
  proto.arrayBuffer = function (this: Blob) {
    return read(this, 'arrayBuffer') as unknown as Promise<ArrayBuffer>;
  };
}
