import buffData from "../../json/buff.json";

export interface BuffInfo {
  name: string;
  summary: string;
  effect: string;
}

type BuffMap = Record<string, BuffInfo>;

export const useBuffInfo = () => {
  const normalize = (data: any): BuffMap => {
    const result: BuffMap = {};

    for (const key in data) {
      const item = data[key];
      result[key] = {
        name: item.name,
        summary: item.summary ?? "",
        effect: item.effect ?? "",
      };
    }

    return result;
  };
  return normalize(buffData);
};
