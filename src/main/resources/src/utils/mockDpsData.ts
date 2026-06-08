const MOCK_HISTORY_DATA = [
  {
    first: 0,
    second: {
      contributors: [
        { id: 1, nickname: "나", server: 1001, job: "검성", isExecutor: true },
        { id: 2, nickname: "딜러A", server: 1002, job: "마도성" },
        { id: 3, nickname: "딜러B", server: 2001, job: "정령성" },
      ],
      battleStart: Date.now() - 120000,
      battleEnd: Date.now() - 90000,
      information: {
        "1": { amount: 1200000, dps: 45000, contribution: 40.0 },
        "2": { amount: 980000, dps: 38000, contribution: 32.5 },
        "3": { amount: 820000, dps: 31000, contribution: 27.5 },
      },
      target: { id: 25166, mob: { code: 2980139, name: "카이시넬의 환영", boss: true } },
    },
  },
  {
    first: 1,
    second: {
      contributors: [
        { id: 1, nickname: "나", server: 1001, job: "검성", isExecutor: true },
        { id: 4, nickname: "딜러C", server: 1003, job: "호법성" },
      ],
      battleStart: Date.now() - 300000,
      battleEnd: Date.now() - 240000,
      information: {
        "1": { amount: 750000, dps: 30000, contribution: 55.0 },
        "4": { amount: 620000, dps: 25000, contribution: 45.0 },
      },
      target: null,
    },
  },
  {
    first: 2,
    second: {
      contributors: [
        { id: 1, nickname: "나", server: 1001, job: "검성", isExecutor: true },
        { id: 2, nickname: "딜러A", server: 1002, job: "마도성" },
        { id: 3, nickname: "딜러B", server: 2001, job: "정령성" },
        { id: 5, nickname: "서폿A", server: 1004, job: "치유성" },
      ],
      battleStart: Date.now() - 600000,
      battleEnd: Date.now() - 540000,
      information: {
        "1": { amount: 2100000, dps: 55000, contribution: 38.0 },
        "2": { amount: 1800000, dps: 48000, contribution: 32.5 },
        "3": { amount: 1500000, dps: 40000, contribution: 27.0 },
        "5": { amount: 150000, dps: 5000, contribution: 2.5 },
      },
      target: { id: 25937, mob: { code: 2980140, name: "바고트", boss: true } },
    },
  },
  {
    first: 0,
    second: {
      contributors: [
        { id: 1, nickname: "나", server: 1001, job: "검성", isExecutor: true },
        { id: 2, nickname: "딜러A", server: 1002, job: "마도성" },
        { id: 3, nickname: "딜러B", server: 2001, job: "정령성" },
      ],
      battleStart: Date.now() - 120000,
      battleEnd: Date.now() - 90000,
      information: {
        "1": { amount: 1200000, dps: 45000, contribution: 40.0 },
        "2": { amount: 980000, dps: 38000, contribution: 32.5 },
        "3": { amount: 820000, dps: 31000, contribution: 27.5 },
      },
      target: { id: 25166, mob: { code: 2980139, name: "카이시넬의 환영", boss: true } },
    },
  },
  {
    first: 1,
    second: {
      contributors: [
        { id: 1, nickname: "나", server: 1001, job: "검성", isExecutor: true },
        { id: 4, nickname: "딜러C", server: 1003, job: "호법성" },
      ],
      battleStart: Date.now() - 300000,
      battleEnd: Date.now() - 240000,
      information: {
        "1": { amount: 750000, dps: 30000, contribution: 55.0 },
        "4": { amount: 620000, dps: 25000, contribution: 45.0 },
      },
      target: null,
    },
  },
  {
    first: 2,
    second: {
      contributors: [
        { id: 1, nickname: "나", server: 1001, job: "검성", isExecutor: true },
        { id: 2, nickname: "딜러A", server: 1002, job: "마도성" },
        { id: 3, nickname: "딜러B", server: 2001, job: "정령성" },
        { id: 5, nickname: "서폿A", server: 1004, job: "치유성" },
      ],
      battleStart: Date.now() - 600000,
      battleEnd: Date.now() - 540000,
      information: {
        "1": { amount: 2100000, dps: 55000, contribution: 38.0 },
        "2": { amount: 1800000, dps: 48000, contribution: 32.5 },
        "3": { amount: 1500000, dps: 40000, contribution: 27.0 },
        "5": { amount: 150000, dps: 5000, contribution: 2.5 },
      },
      target: { id: 25937, mob: { code: 2980140, name: "바고트", boss: true } },
    },
  },
  {
    first: 0,
    second: {
      contributors: [
        { id: 1, nickname: "나", server: 1001, job: "검성", isExecutor: true },
        { id: 2, nickname: "딜러A", server: 1002, job: "마도성" },
        { id: 3, nickname: "딜러B", server: 2001, job: "정령성" },
      ],
      battleStart: Date.now() - 120000,
      battleEnd: Date.now() - 90000,
      information: {
        "1": { amount: 1200000, dps: 45000, contribution: 40.0 },
        "2": { amount: 980000, dps: 38000, contribution: 32.5 },
        "3": { amount: 820000, dps: 31000, contribution: 27.5 },
      },
      target: { id: 25166, mob: { code: 2980139, name: "카이시넬의 환영", boss: true } },
    },
  },
  {
    first: 1,
    second: {
      contributors: [
        { id: 1, nickname: "나", server: 1001, job: "검성", isExecutor: true },
        { id: 4, nickname: "딜러C", server: 1003, job: "호법성" },
      ],
      battleStart: Date.now() - 300000,
      battleEnd: Date.now() - 240000,
      information: {
        "1": { amount: 750000, dps: 30000, contribution: 55.0 },
        "4": { amount: 620000, dps: 25000, contribution: 45.0 },
      },
      target: null,
    },
  },
  {
    first: 2,
    second: {
      contributors: [
        { id: 1, nickname: "나", server: 1001, job: "검성", isExecutor: true },
        { id: 2, nickname: "딜러A", server: 1002, job: "마도성" },
        { id: 3, nickname: "딜러B", server: 2001, job: "정령성" },
        { id: 5, nickname: "서폿A", server: 1004, job: "치유성" },
      ],
      battleStart: Date.now() - 600000,
      battleEnd: Date.now() - 540000,
      information: {
        "1": { amount: 2100000, dps: 55000, contribution: 38.0 },
        "2": { amount: 1800000, dps: 48000, contribution: 32.5 },
        "3": { amount: 1500000, dps: 40000, contribution: 27.0 },
        "5": { amount: 150000, dps: 5000, contribution: 2.5 },
      },
      target: { id: 25937, mob: { code: 2980140, name: "바고트", boss: true } },
    },
  },
  {
    first: 0,
    second: {
      contributors: [
        { id: 1, nickname: "나", server: 1001, job: "검성", isExecutor: true },
        { id: 2, nickname: "딜러A", server: 1002, job: "마도성" },
        { id: 3, nickname: "딜러B", server: 2001, job: "정령성" },
      ],
      battleStart: Date.now() - 120000,
      battleEnd: Date.now() - 90000,
      information: {
        "1": { amount: 1200000, dps: 45000, contribution: 40.0 },
        "2": { amount: 980000, dps: 38000, contribution: 32.5 },
        "3": { amount: 820000, dps: 31000, contribution: 27.5 },
      },
      target: { id: 25166, mob: { code: 2980139, name: "카이시넬의 환영", boss: true } },
    },
  },
  {
    first: 1,
    second: {
      contributors: [
        { id: 1, nickname: "나", server: 1001, job: "검성", isExecutor: true },
        { id: 4, nickname: "딜러C", server: 1003, job: "호법성" },
      ],
      battleStart: Date.now() - 300000,
      battleEnd: Date.now() - 240000,
      information: {
        "1": { amount: 750000, dps: 30000, contribution: 55.0 },
        "4": { amount: 620000, dps: 25000, contribution: 45.0 },
      },
      target: null,
    },
  },
  {
    first: 2,
    second: {
      contributors: [
        { id: 1, nickname: "나", server: 1001, job: "검성", isExecutor: true },
        { id: 2, nickname: "딜러A", server: 1002, job: "마도성" },
        { id: 3, nickname: "딜러B", server: 2001, job: "정령성" },
        { id: 5, nickname: "서폿A", server: 1004, job: "치유성" },
      ],
      battleStart: Date.now() - 600000,
      battleEnd: Date.now() - 540000,
      information: {
        "1": { amount: 2100000, dps: 55000, contribution: 38.0 },
        "2": { amount: 1800000, dps: 48000, contribution: 32.5 },
        "3": { amount: 1500000, dps: 40000, contribution: 27.0 },
        "5": { amount: 150000, dps: 5000, contribution: 2.5 },
      },
      target: { id: 25937, mob: { code: 2980140, name: "바고트", boss: true } },
    },
  },
  {
    first: 0,
    second: {
      contributors: [
        { id: 1, nickname: "나", server: 1001, job: "검성", isExecutor: true },
        { id: 2, nickname: "딜러A", server: 1002, job: "마도성" },
        { id: 3, nickname: "딜러B", server: 2001, job: "정령성" },
      ],
      battleStart: Date.now() - 120000,
      battleEnd: Date.now() - 90000,
      information: {
        "1": { amount: 1200000, dps: 45000, contribution: 40.0 },
        "2": { amount: 980000, dps: 38000, contribution: 32.5 },
        "3": { amount: 820000, dps: 31000, contribution: 27.5 },
      },
      target: { id: 25166, mob: { code: 2980139, name: "카이시넬의 환영", boss: true } },
    },
  },
  {
    first: 1,
    second: {
      contributors: [
        { id: 1, nickname: "나", server: 1001, job: "검성", isExecutor: true },
        { id: 4, nickname: "딜러C", server: 1003, job: "호법성" },
      ],
      battleStart: Date.now() - 300000,
      battleEnd: Date.now() - 240000,
      information: {
        "1": { amount: 750000, dps: 30000, contribution: 55.0 },
        "4": { amount: 620000, dps: 25000, contribution: 45.0 },
      },
      target: null,
    },
  },
  {
    first: 2,
    second: {
      contributors: [
        { id: 1, nickname: "나", server: 1001, job: "검성", isExecutor: true },
        { id: 2, nickname: "딜러A", server: 1002, job: "마도성" },
        { id: 3, nickname: "딜러B", server: 2001, job: "정령성" },
        { id: 5, nickname: "서폿A", server: 1004, job: "치유성" },
      ],
      battleStart: Date.now() - 600000,
      battleEnd: Date.now() - 540000,
      information: {
        "1": { amount: 2100000, dps: 55000, contribution: 38.0 },
        "2": { amount: 1800000, dps: 48000, contribution: 32.5 },
        "3": { amount: 1500000, dps: 40000, contribution: 27.0 },
        "5": { amount: 150000, dps: 5000, contribution: 2.5 },
      },
      target: { id: 25937, mob: { code: 2980140, name: "바고트", boss: true } },
    },
  },
  {
    first: 0,
    second: {
      contributors: [
        { id: 1, nickname: "나", server: 1001, job: "검성", isExecutor: true },
        { id: 2, nickname: "딜러A", server: 1002, job: "마도성" },
        { id: 3, nickname: "딜러B", server: 2001, job: "정령성" },
      ],
      battleStart: Date.now() - 120000,
      battleEnd: Date.now() - 90000,
      information: {
        "1": { amount: 1200000, dps: 45000, contribution: 40.0 },
        "2": { amount: 980000, dps: 38000, contribution: 32.5 },
        "3": { amount: 820000, dps: 31000, contribution: 27.5 },
      },
      target: { id: 25166, mob: { code: 2980139, name: "카이시넬의 환영", boss: true } },
    },
  },
  {
    first: 1,
    second: {
      contributors: [
        { id: 1, nickname: "나", server: 1001, job: "검성", isExecutor: true },
        { id: 4, nickname: "딜러C", server: 1003, job: "호법성" },
      ],
      battleStart: Date.now() - 300000,
      battleEnd: Date.now() - 240000,
      information: {
        "1": { amount: 750000, dps: 30000, contribution: 55.0 },
        "4": { amount: 620000, dps: 25000, contribution: 45.0 },
      },
      target: null,
    },
  },
  {
    first: 2,
    second: {
      contributors: [
        { id: 1, nickname: "나", server: 1001, job: "검성", isExecutor: true },
        { id: 2, nickname: "딜러A", server: 1002, job: "마도성" },
        { id: 3, nickname: "딜러B", server: 2001, job: "정령성" },
        { id: 5, nickname: "서폿A", server: 1004, job: "치유성" },
      ],
      battleStart: Date.now() - 600000,
      battleEnd: Date.now() - 540000,
      information: {
        "1": { amount: 2100000, dps: 55000, contribution: 38.0 },
        "2": { amount: 1800000, dps: 48000, contribution: 32.5 },
        "3": { amount: 1500000, dps: 40000, contribution: 27.0 },
        "5": { amount: 150000, dps: 5000, contribution: 2.5 },
      },
      target: { id: 25937, mob: { code: 2980140, name: "바고트", boss: true } },
    },
  },
  {
    first: 0,
    second: {
      contributors: [
        { id: 1, nickname: "나", server: 1001, job: "검성", isExecutor: true },
        { id: 2, nickname: "딜러A", server: 1002, job: "마도성" },
        { id: 3, nickname: "딜러B", server: 2001, job: "정령성" },
      ],
      battleStart: Date.now() - 120000,
      battleEnd: Date.now() - 90000,
      information: {
        "1": { amount: 1200000, dps: 45000, contribution: 40.0 },
        "2": { amount: 980000, dps: 38000, contribution: 32.5 },
        "3": { amount: 820000, dps: 31000, contribution: 27.5 },
      },
      target: { id: 25166, mob: { code: 2980139, name: "카이시넬의 환영", boss: true } },
    },
  },
  {
    first: 1,
    second: {
      contributors: [
        { id: 1, nickname: "나", server: 1001, job: "검성", isExecutor: true },
        { id: 4, nickname: "딜러C", server: 1003, job: "호법성" },
      ],
      battleStart: Date.now() - 300000,
      battleEnd: Date.now() - 240000,
      information: {
        "1": { amount: 750000, dps: 30000, contribution: 55.0 },
        "4": { amount: 620000, dps: 25000, contribution: 45.0 },
      },
      target: null,
    },
  },
  {
    first: 2,
    second: {
      contributors: [
        { id: 1, nickname: "나", server: 1001, job: "검성", isExecutor: true },
        { id: 2, nickname: "딜러A", server: 1002, job: "마도성" },
        { id: 3, nickname: "딜러B", server: 2001, job: "정령성" },
        { id: 5, nickname: "서폿A", server: 1004, job: "치유성" },
      ],
      battleStart: Date.now() - 600000,
      battleEnd: Date.now() - 540000,
      information: {
        "1": { amount: 2100000, dps: 55000, contribution: 38.0 },
        "2": { amount: 1800000, dps: 48000, contribution: 32.5 },
        "3": { amount: 1500000, dps: 40000, contribution: 27.0 },
        "5": { amount: 150000, dps: 5000, contribution: 2.5 },
      },
      target: { id: 25937, mob: { code: 2980140, name: "바고트", boss: true } },
    },
  },
  {
    first: 0,
    second: {
      contributors: [
        { id: 1, nickname: "나", server: 1001, job: "검성", isExecutor: true },
        { id: 2, nickname: "딜러A", server: 1002, job: "마도성" },
        { id: 3, nickname: "딜러B", server: 2001, job: "정령성" },
      ],
      battleStart: Date.now() - 120000,
      battleEnd: Date.now() - 90000,
      information: {
        "1": { amount: 1200000, dps: 45000, contribution: 40.0 },
        "2": { amount: 980000, dps: 38000, contribution: 32.5 },
        "3": { amount: 820000, dps: 31000, contribution: 27.5 },
      },
      target: { id: 25166, mob: { code: 2980139, name: "카이시넬의 환영", boss: true } },
    },
  },
  {
    first: 1,
    second: {
      contributors: [
        { id: 1, nickname: "나", server: 1001, job: "검성", isExecutor: true },
        { id: 4, nickname: "딜러C", server: 1003, job: "호법성" },
      ],
      battleStart: Date.now() - 300000,
      battleEnd: Date.now() - 240000,
      information: {
        "1": { amount: 750000, dps: 30000, contribution: 55.0 },
        "4": { amount: 620000, dps: 25000, contribution: 45.0 },
      },
      target: null,
    },
  },
  {
    first: 2,
    second: {
      contributors: [
        { id: 1, nickname: "나", server: 1001, job: "검성", isExecutor: true },
        { id: 2, nickname: "딜러A", server: 1002, job: "마도성" },
        { id: 3, nickname: "딜러B", server: 2001, job: "정령성" },
        { id: 5, nickname: "서폿A", server: 1004, job: "치유성" },
      ],
      battleStart: Date.now() - 600000,
      battleEnd: Date.now() - 540000,
      information: {
        "1": { amount: 2100000, dps: 55000, contribution: 38.0 },
        "2": { amount: 1800000, dps: 48000, contribution: 32.5 },
        "3": { amount: 1500000, dps: 40000, contribution: 27.0 },
        "5": { amount: 150000, dps: 5000, contribution: 2.5 },
      },
      target: { id: 25937, mob: { code: 2980140, name: "바고트", boss: true } },
    },
  },
];

const MOCK_DETAIL_DATA: Record<string, Record<string, unknown>> = {
  "14310000": {
    skillName: "바이젤의 권능",
    times: 45,
    damageAmount: 1200000,
    critTimes: 30,
    parryTimes: 5,
    backTimes: 20,
    perfectTimes: 10,
    doubleTimes: 8,
    shardTimes: 5,
    dotDamageAmount: 150000,
    dotTimes: 45,
  },
  "14050000": {
    skillName: "송곳 화살",
    times: 38,
    damageAmount: 980000,
    critTimes: 22,
    parryTimes: 3,
    shardTimes: 2,
    backTimes: 15,
    perfectTimes: 7,
    doubleTimes: 5,
    dotDamageAmount: 0,
    dotTimes: 0,
  },
  "14060000": {
    skillName: "그리폰 화살",
    times: 20,
    damageAmount: 750000,
    critTimes: 12,
    shardTimes: 1,
    parryTimes: 2,
    backTimes: 8,
    perfectTimes: 4,
    doubleTimes: 3,
    dotDamageAmount: 0,
    dotTimes: 0,
  },
  "14360000": {
    skillName: "폭발 화살",
    times: 60,
    damageAmount: 520000,
    critTimes: 40,
    shardTimes: 5,
    parryTimes: 0,
    backTimes: 25,
    perfectTimes: 15,
    doubleTimes: 12,
    dotDamageAmount: 80000,
    dotTimes: 60,
  },
  "14380000": {
    skillName: "지원 사격",
    times: 10,
    damageAmount: 200000,
    critTimes: 3,
    shardTimes: 234455,
    parryTimes: 8,
    backTimes: 1,
    perfectTimes: 2,
    doubleTimes: 1,
    dotDamageAmount: 0,
    dotTimes: 0,
  },
  "14270000": {
    skillName: "화살 폭풍",
    times: 38,
    damageAmount: 980000,
    shardTimes: 222,
    critTimes: 22,
    parryTimes: 3,
    backTimes: 15,
    perfectTimes: 7,
    doubleTimes: 5,
    dotDamageAmount: 0,
    dotTimes: 0,
  },
  "14700000": {
    skillName: "강습 강타",
    times: 20,
    damageAmount: 750000,
    critTimes: 12,
    parryTimes: 2,
    backTimes: 8,
    perfectTimes: 4,
    doubleTimes: 3,
    dotDamageAmount: 0,
    dotTimes: 0,
  },
  "14340000": {
    skillName: "속사",
    times: 60,
    damageAmount: 520000,
    critTimes: 40,
    parryTimes: 1,
    backTimes: 25,
    perfectTimes: 15,
    doubleTimes: 12,
    dotDamageAmount: 80000,
    dotTimes: 60,
  },
  "14740000": {
    skillName: "집중의 눈",
    times: 10,
    damageAmount: 200000,
    critTimes: 3,
    parryTimes: 8,
    backTimes: 1,
    perfectTimes: 2,
    doubleTimes: 1,
    dotDamageAmount: 0,
    dotTimes: 0,
  },
  "14010000": {
    skillName: "조준 화살",
    times: 38,
    damageAmount: 980000,
    critTimes: 22,
    parryTimes: 3,
    backTimes: 15,
    perfectTimes: 7,
    doubleTimes: 5,
    dotDamageAmount: 0,
    dotTimes: 0,
  },
  "14750000": {
    skillName: "사냥꾼의 결의",
    times: 20,
    damageAmount: 750000,
    critTimes: 12,
    parryTimes: 2,
    backTimes: 8,
    perfectTimes: 4,
    doubleTimes: 3,
    dotDamageAmount: 0,
    dotTimes: 0,
  },
  "14110000": {
    skillName: "파열 화살",
    times: 60,
    damageAmount: 520000,
    critTimes: 40,
    parryTimes: 1,
    backTimes: 25,
    perfectTimes: 15,
    doubleTimes: 12,
    dotDamageAmount: 80000,
    dotTimes: 60,
  },
  "14090000": {
    skillName: "사냥꾼의 혼",
    times: 10,
    damageAmount: 200000,
    critTimes: 3,
    parryTimes: 8,
    backTimes: 1,
    perfectTimes: 2,
    doubleTimes: 1,
    dotDamageAmount: 0,
    dotTimes: 0,
  },
};

const makeMockSkill = (
  skillName: string,
  damageAmount: number,
  times = 24,
  critTimes = 14,
  doubleTimes = 5,
): Record<string, unknown> => ({
  skillName,
  times,
  damageAmount,
  shardTimes: Math.max(0, Math.floor(times / 4)),
  critTimes,
  parryTimes: Math.max(0, Math.floor(times / 10)),
  backTimes: Math.max(0, Math.floor(times / 3)),
  perfectTimes: Math.max(0, Math.floor(times / 5)),
  doubleTimes,
  dotDamageAmount: 0,
  dotTimes: 0,
});

const detailFromSkills = (entries: [string, string, number, number?, number?, number?][]) =>
  Object.fromEntries(entries.map(([code, name, dmg, times, crit, double]) => [code, makeMockSkill(name, dmg, times, crit, double)]));

const MOCK_DETAIL_BY_PLAYER: Record<string, Record<string, unknown>> = {
  "1": detailFromSkills([
    ["11020000", "예리한 일격", 1200000, 45, 30, 8],
    ["11030000", "파열의 일격", 980000, 38, 22, 5],
    ["11040000", "분노의 일격", 760000, 28, 18, 4],
    ["11280000", "검기 난무", 520000, 18, 11, 3],
    ["11010047", "격파의 맹타", 340000, 12, 8, 2],
  ]),
  "2": detailFromSkills([
    ["12010000", "맹렬한 일격", 1120000, 42, 26, 7],
    ["12020000", "회심의 일격", 900000, 33, 19, 5],
    ["12030000", "필사의 일격", 720000, 24, 16, 4],
    ["12200000", "균형의 갑옷", 460000, 18, 9, 2],
    ["12760000", "충격 적중", 320000, 14, 7, 2],
    ["12790000", "생존 의지", 220000, 10, 5, 1],
  ]),
  "3": detailFromSkills([
    ["13010000", "빠른 베기", 1180000, 44, 29, 9],
    ["13030000", "절혼 베기", 940000, 36, 24, 7],
    ["13040000", "쾌속 베기", 770000, 30, 19, 5],
    ["13210000", "침투", 520000, 20, 12, 3],
    ["13330000", "폭풍 베기", 310000, 13, 8, 2],
  ]),
  "4": MOCK_DETAIL_DATA,
  "5": detailFromSkills([
    ["15010000", "화염 난사", 1160000, 40, 27, 8],
    ["15030000", "작렬", 960000, 35, 23, 6],
    ["15250000", "열화", 780000, 28, 18, 5],
    ["15340000", "저주: 고목", 540000, 18, 10, 3],
    ["15730000", "냉기 소환", 300000, 12, 7, 2],
  ]),
  "6": detailFromSkills([
    ["16010000", "냉기 충격", 1110000, 42, 24, 7],
    ["16020000", "진공 폭발", 880000, 31, 18, 5],
    ["16030000", "대지 진동", 730000, 24, 15, 4],
    ["16210000", "절망의 저주", 460000, 16, 9, 2],
    ["16170000", "카이시넬의 권능", 280000, 10, 5, 1],
  ]),
  "7": detailFromSkills([
    ["17010000", "대지의 응보", 860000, 32, 20, 5],
    ["17020000", "뇌전", 720000, 25, 15, 4],
    ["17030000", "방전", 590000, 21, 12, 3],
    ["17050000", "천벌", 420000, 14, 8, 2],
    ["17760000", "충격 적중", 260000, 10, 5, 1],
  ]),
  "8": detailFromSkills([
    ["18010000", "격파쇄", 940000, 35, 21, 5],
    ["18020000", "공명쇄", 790000, 28, 17, 4],
    ["18030000", "벽력쇄", 640000, 22, 13, 3],
    ["18300000", "질풍 난무", 470000, 16, 9, 2],
    ["18790000", "생존 의지", 250000, 10, 5, 1],
  ]),
};
type MockBuffEntry = {
  code: string;
  name: string;
  summary: string;
  effect: string;
  operatingRate: number;
  actorId: number;
  actorName?: string;
};

const toBuffCode = (skillCode: number) => `${skillCode}1`;

const mockBuff = (
  skillCode: number,
  name: string,
  operatingRate: number,
  actorId: number,
  summary: string,
  effect: string,
  actorName?: string,
): MockBuffEntry => ({
  code: toBuffCode(skillCode),
  name,
  summary,
  effect,
  operatingRate,
  actorId,
  actorName,
});

const MOCK_PARTY_BUFFS: MockBuffEntry[] = [
  mockBuff(
    18190000,
    "불패의 진언",
    99.3,
    8,
    "파티 생존 보조",
    "파티원의 방어 성능을 높여주는 진언입니다.",
    "호법[테스트]",
  ),
  mockBuff(
    18160000,
    "질주의 진언",
    92.8,
    8,
    "기동성 보조",
    "파티원의 이동 관련 능력을 보조합니다.",
    "호법[테스트]",
  ),
  mockBuff(
    18420000,
    "수호의 축복",
    74.4,
    8,
    "피해 완화",
    "짧은 시간 동안 받는 피해를 줄여줍니다.",
    "호법[테스트]",
  ),
  mockBuff(
    17410000,
    "보호의 빛",
    66.7,
    7,
    "보호막",
    "대상에게 보호 효과를 부여합니다.",
    "치유[테스트]",
  ),
  mockBuff(
    17420000,
    "유스티엘의 권능",
    41.9,
    7,
    "회복 강화",
    "회복과 생존을 보조하는 권능 효과입니다.",
    "치유[테스트]",
  ),
];

const MOCK_BUFF_BY_PLAYER: Record<string, MockBuffEntry[]> = {
  "1": [
    mockBuff(11250000, "지켈의 축복", 98.7, 1, "공격 강화", "검성의 순간 화력을 끌어올립니다.", "나[검성]"),
    mockBuff(11750000, "공격 준비", 86.2, 1, "전투 준비", "공격 능력치를 끌어올리는 자기 강화입니다.", "나[검성]"),
    mockBuff(11800000, "살기 파열", 51.4, 1, "연계 강화", "살기 파열 상태에서 일부 공격 흐름을 강화합니다.", "나[검성]"),
    ...MOCK_PARTY_BUFFS,
  ],
  "2": [
    mockBuff(12200000, "균형의 갑옷", 93.5, 2, "상태 이상 저항", "수호성의 안정성을 높이는 자기 강화입니다.", "수호[테스트]"),
    mockBuff(12110000, "보호의 방패", 58.1, 2, "방어 강화", "방패로 받는 피해를 줄입니다.", "수호[테스트]"),
    ...MOCK_PARTY_BUFFS,
  ],
  "3": [
    mockBuff(13750000, "강습 자세", 88.4, 3, "공격 자세", "살성의 공격 흐름을 강화합니다.", "살성[테스트]"),
    mockBuff(13390000, "신속의 계약", 63.8, 3, "신속 강화", "전투 중 기동성과 공격 템포를 끌어올립니다.", "살성[테스트]"),
    ...MOCK_PARTY_BUFFS,
  ],
  "4": [
    mockBuff(14740000, "집중의 눈", 96.1, 4, "명중 보조", "궁성의 정확도와 안정적인 딜링을 보조합니다.", "궁성[테스트]"),
    mockBuff(14750000, "사냥꾼의 결의", 72.6, 4, "전투 집중", "사냥꾼의 전투 능력을 끌어올립니다.", "궁성[테스트]"),
    ...MOCK_PARTY_BUFFS,
  ],
  "5": [
    mockBuff(15740000, "불꽃의 로브", 97.5, 5, "마법 강화", "마도성의 화염 계열 운용을 보조합니다.", "마도[테스트]"),
    mockBuff(15160000, "강철 보호막", 46.8, 5, "보호막", "일정 피해를 흡수합니다.", "마도[테스트]"),
    ...MOCK_PARTY_BUFFS,
  ],
  "6": [
    mockBuff(16370000, "불길의 축복", 89.2, 6, "정령 강화", "소환 정령과 시전자 전투를 보조합니다.", "정령[테스트]"),
    mockBuff(16190000, "강화: 정령의 가호", 57.3, 6, "정령 보호", "정령성의 생존과 운용을 보조합니다.", "정령[테스트]"),
    ...MOCK_PARTY_BUFFS,
  ],
  "7": [
    mockBuff(17160000, "생명의 권능", 91.8, 7, "회복 권능", "회복 운용을 강화합니다.", "치유[테스트]"),
    mockBuff(17410000, "보호의 빛", 69.1, 7, "보호막", "대상에게 보호 효과를 부여합니다.", "치유[테스트]"),
    ...MOCK_PARTY_BUFFS,
  ],
  "8": [
    mockBuff(18190000, "불패의 진언", 99.3, 8, "파티 생존 보조", "파티원의 방어 성능을 높여주는 진언입니다.", "호법[테스트]"),
    mockBuff(18160000, "질주의 진언", 92.8, 8, "기동성 보조", "파티원의 이동 관련 능력을 보조합니다.", "호법[테스트]"),
    mockBuff(18250000, "질풍의 권능", 62.2, 8, "공격 지원", "파티원의 전투 템포를 보조합니다.", "호법[테스트]"),
    ...MOCK_PARTY_BUFFS,
  ],
};

const getMockBuffData = (id: number | string): MockBuffEntry[] =>
  MOCK_BUFF_BY_PLAYER[String(id)] ?? MOCK_BUFF_BY_PLAYER["1"];

const MOCK_DEBUFF_DATA: MockBuffEntry[] = [
  mockBuff(14180000, "결박의 덫", 76.8, 4, "이동 제한", "대상 이동을 제한하는 덫 효과입니다.", "궁성[테스트]"),
  mockBuff(14160000, "봉인 화살", 48.4, 4, "스킬 방해", "대상 행동을 방해하는 화살 효과입니다.", "궁성[테스트]"),
  mockBuff(16220000, "저주의 구름", 82.5, 6, "저주", "대상에게 지속적인 약화 효과를 남깁니다.", "정령[테스트]"),
  mockBuff(15140000, "저주: 나무", 34.6, 5, "변이", "대상을 나무로 만드는 약화 효과입니다.", "마도[테스트]"),
  mockBuff(12120000, "도발", 23.1, 2, "위협", "대상의 주의를 끌어 전투 흐름을 제어합니다.", "수호[테스트]"),
];
const MOCK_DATA = {
  contributors: [
    { id: 1, nickname: "나[검성]", server: 1001, power: 234123, job: "검성", isExecutor: true },
    { id: 2, nickname: "수호[테스트]", power: 564123, server: 2002, job: "수호성" },
    { id: 3, nickname: "살성[테스트]", power: 413456, job: "살성" },
    { id: 4, nickname: "궁성[테스트]", server: 1003, power: 442123, job: "궁성" },
    { id: 5, nickname: "마도[테스트]", server: 1004, power: 379000, job: "마도성" },
    { id: 6, nickname: "정령[테스트]", server: 1005, power: 339500, job: "정령성" },
    { id: 7, nickname: "치유[테스트]", server: 1005, power: 239500, job: "치유성" },
    { id: 8, nickname: "호법[테스트]", server: 1005, power: 239500, job: "호법성" },
  ],
  battleStart: Date.now() - 93000,
  battleEnd: Date.now(),
  information: {
    "1": { amount: 4185000, dps: 995642, contribution: 20.4 },
    "2": { amount: 3650000, dps: 870000, contribution: 17.8 },
    "3": { amount: 3120000, dps: 743000, contribution: 15.2 },
    "4": { amount: 2790000, dps: 724230, contribution: 13.6 },
    "5": { amount: 2290000, dps: 545000, contribution: 11.2 },
    "6": { amount: 1980000, dps: 471000, contribution: 9.7 },
    "7": { amount: 1280000, dps: 304000, contribution: 6.3 },
    "8": { amount: 1180000, dps: 281000, contribution: 5.8 },
  },
  target: {
    id: 25166,
    mob: { code: 2980139, name: "계약파괴자 가르투아", boss: true },
    remainHp: 4368556,
    maxHp: 7560000,
  },
};

export const injectMockDpsData = () => {
  if ((window as any).javaBridge) return;

  const props = new Map<string, string>([
    ["meterWidth", "430"],
    ["rowHeight", "38"],
    ["meterOpacity", "0.82"],
    ["displayMode", "amount_dps_percent"],
    ["damageValueMode", "dps"],
    ["targetInfoDisplayMode", "hp_percent"],
    ["nameDisplay", "all"],
    ["fontFamily", "NEXON Lv2 Gothic"],
    ["overlayTheme", "dark"],
    ["overlayLayout", "standard"],
    ["showCombatTimerInMinimal", "true"],
    ["showTargetInfoInMinimal", "true"],
    ["contributionMode", "contribution"],
    ["statsConsentState", "unknown"],
    ["statsUploadEnabled", "false"],
    ["statsPublicCharacter", "true"],
    ["statsConsentUpdatedAt", "0"],
  ]);

  const readStatsConsent = () =>
    JSON.stringify({
      state: props.get("statsConsentState") ?? "unknown",
      uploadEnabled: props.get("statsUploadEnabled") === "true",
      publicCharacter: props.get("statsPublicCharacter") !== "false",
      consentVersion: "2026-06-04",
      updatedAt: Number(props.get("statsConsentUpdatedAt") ?? 0),
    });

  (window as any).javaBridge = {
    loadProps: (key: string) => props.get(key) ?? "",
    saveProps: (key: string, value: string) => props.set(key, String(value)),
    moveWindow: () => undefined,
    getHideHotkey: () => "",
    getClickThroughHotkey: () => "",
    updateHideHotkey: () => undefined,
    updateClickThroughHotkey: () => undefined,
    isDebuggingMode: () => false,
    isClickThrough: () => false,
    isAutoHide: () => true,
    toggleAutoHide: () => undefined,
    getStatsConsent: readStatsConsent,
    setStatsConsent: (state: string, uploadEnabled: boolean, publicCharacter: boolean) => {
      props.set("statsConsentState", state);
      props.set("statsUploadEnabled", String(uploadEnabled));
      props.set("statsPublicCharacter", String(publicCharacter));
      props.set("statsConsentUpdatedAt", String(Date.now()));
      return readStatsConsent();
    },
    getStatsOwnCharacter: () =>
      JSON.stringify({
        detected: true,
        id: 1,
        nickname: "큰팡",
        server: 1001,
        job: "궁성",
        power: 4180000,
      }),
    getStatsUploadStatus: () =>
      JSON.stringify({
        enabled: props.get("statsUploadEnabled") === "true",
        pending: 0,
        uploaded: props.get("statsUploadEnabled") === "true" ? 1 : 0,
        skipped: 2,
        failed: 0,
        lastPath:
          props.get("statsUploadEnabled") === "true"
            ? "http://34.64.49.225/api/v1/reports"
            : "",
        lastReason:
          props.get("statsUploadEnabled") === "true"
            ? "uploaded:200"
            : "동의 후 보스 처치 전투만 업로드 후보로 저장됩니다.",
        lastUpdatedAt: Date.now(),
      }),
    openStatsUploadFolder: () =>
      "C:\\Users\\Waffle\\AppData\\Roaming\\waffle_meter.v1.6\\stats-upload",
    getDpsData: () => JSON.stringify(MOCK_DATA),
    getBattleDetail: (id: string) => JSON.stringify(MOCK_DETAIL_BY_PLAYER[String(id)] ?? MOCK_DETAIL_DATA),
    getBattleDetailFromList: (_idx: number, uid: number) => JSON.stringify(MOCK_DETAIL_BY_PLAYER[String(uid)] ?? MOCK_DETAIL_DATA),
    getBattleList: () => JSON.stringify(MOCK_HISTORY_DATA),
    getVersion: () => "1.7.4",
    getLiveBuffOperatingRate: (id: number) => JSON.stringify(getMockBuffData(id)),
    getBuffOperatingRate: (_idx: number, id: number) => JSON.stringify(getMockBuffData(id)),
    openBrowser: (url: string) => console.log("[mock] openBrowser:", url),
    getLiveBossBuffOperatingRate: (_id: number) => JSON.stringify(MOCK_DEBUFF_DATA),
    getBossBuffOperatingRate: (_idx: number, _id: number) => JSON.stringify(MOCK_DEBUFF_DATA),

    exitApp: () => console.log("[mock] exitApp"),
    hideToTray: () => console.log("[mock] hideToTray"),
    hardResetDps: () => console.log("[mock] hardResetDps"),
    startUpdate: (url: string) => {
      console.log("[mock] startUpdate:", url);
      let percent = 0;
      const timer = setInterval(() => {
        percent += Math.floor(Math.random() * 10 + 5);
        if (percent >= 100) {
          percent = 100;
          clearInterval(timer);
          setTimeout(() => (window as any).onDownloadComplete?.(), 500);
        }
        (window as any).onDownloadProgress?.(percent);
      }, 400);
    },
    applyUpdate: () => console.log("[mock] applyUpdate"),
    armUpdateOnExit: () => console.log("[mock] armUpdateOnExit"),
  };
  const MOCK_JOIN_REQUESTS = [
    {
      nickname: "치4유성유저F",
      power: 95000,
      job: "치유성",
      server: 1004,
      requester: 10206,
      skill: {
        "16140340": 13,
        "17160000": 10,
        "1739000": 20,
        "17420000": 12,
        "17080000": 20,
        "17400000": 10,
        "17070000": 5,
        "1741000": 10,
        "17780000": 10,
      },
    },
    {
      nickname: "치2유성유저F",
      power: 95000,
      job: "치유성",
      server: 1004,
      requester: 10016,
      skill: {
        "17090000": 13,
        "17160000": 10,
        "1739000": 20,
        "17420000": 12,
        "17080000": 20,
        "17400000": 10,
        "17070000": 5,
        "1741000": 10,
        "17780000": 10,
      },
    },
    {
      nickname: "검성유저A",
      power: 121,
      job: "검성",
      server: 1001,
      requester: 1001,
      skill: { "11800000": 10, "11250000": 8 },
    },
    {
      nickname: "마도성유저B",
      power: 98000,
      job: "마도성",
      server: 1002,
      requester: 1002,
      skill: { "15110000": 15, "15320000": 12 },
    },
    {
      nickname: "정령성유저C",
      power: 110000,
      job: "정령성",
      server: 2001,
      requester: 1003,
      skill: { "16370000": 9, "16150000": 11 },
    },
    {
      nickname: "궁성유저D",
      power: 132000,
      job: "궁성",
      server: 1003,
      requester: 1004,
      skill: { "14310000": 10, "14220000": 7 },
    },
    { nickname: "살성유저E", power: 88000, job: "살성", server: 2002, requester: 1005, skill: {} },
    {
      nickname: "치유성유저F",
      power: 95000,
      job: "치유성",
      server: 1004,
      requester: 1006,
      skill: {
        "17090000": 13,
        "17160000": 10,
        "1739000": 20,
        "17420000": 12,
        "17080000": 20,
        "17400000": 10,
        "17070000": 5,
        "1741000": 10,
        "17780000": 10,
      },
    },
    {
      nickname: "호법성유저G",
      power: 102000,
      job: "호법성",
      server: 1005,
      requester: 1007,
      skill: { "18080000": 11, "18780000": 9 },
    },
    {
      nickname: "수호성유저H",
      power: 118000,
      job: "수호성",
      server: 2003,
      requester: 1008,
      skill: { "12780000": 10, "12120000": 8 },
    },
    {
      nickname: "정령성유저I",
      power: 76000,
      job: "정령성",
      server: 1006,
      requester: 1009,
      skill: { "16220000": 7 },
    },
    {
      nickname: "검성유저J",
      power: 143000,
      job: "검성",
      server: 2004,
      requester: 1010,
      skill: { "11800000": 12, "11400000": 10 },
    },
  ];

  MOCK_JOIN_REQUESTS.forEach((req, i) => {
    setTimeout(
      () => {
        (window as any).onJoinRequest?.({ ...req, arrivedAt: Date.now() });
      },
      3000 + i * 2000,
    );
  });
};
