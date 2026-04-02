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
      result[key] = {
        name: data[key].name,
        summary: data[key].summary ?? "",
        effect: data[key].effect ?? "",
      };
    }
    return result;
  };
  return normalize(buffData);
};
