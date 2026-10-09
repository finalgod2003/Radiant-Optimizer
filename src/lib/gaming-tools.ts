export function frameTime(fps: number): number | null {
  if (!Number.isFinite(fps) || fps <= 0) return null;
  const result = 1000 / fps;
  return Number.isFinite(result) ? result : null;
}

/** Preserve eDPI within the same game when changing mouse DPI. */
export function convertSensitivity(
  sensitivity: number,
  oldDpi: number,
  newDpi: number
): number | null {
  if (
    [sensitivity, oldDpi, newDpi].some(
      (value) => !Number.isFinite(value) || value <= 0
    )
  )
    return null;
  const result = (sensitivity * oldDpi) / newDpi;
  return Number.isFinite(result) && result > 0 ? result : null;
}
