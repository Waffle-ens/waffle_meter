export const formatAmount = (amount: number): string => {
  const trunc = (n: number, digits: number) => Math.trunc(n * 10 ** digits) / 10 ** digits;
  if (amount >= 1_000_000_000) return `${trunc(amount / 1_000_000_000, 2).toFixed(2)}B`;
  if (amount >= 1_000_000) return `${trunc(amount / 1_000_000, 2).toFixed(2)}M`;
  if (amount >= 1_000) return `${trunc(amount / 1_000, 2).toFixed(2)}K`;
  return String(amount);
};

export const formatPower = (power: number): string => {
  if (!Number.isFinite(power) || power <= 0) return "-";
  return `${(power / 1000).toFixed(1)}k`;
};
