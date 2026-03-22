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
  "1001": {
    skillName: "폭풍검",
    times: 45,
    damageAmount: 1200000,
    critTimes: 30,
    parryTimes: 5,
    backTimes: 20,
    perfectTimes: 10,
    doubleTimes: 8,
    dotDamageAmount: 150000,
    dotTimes: 45,
  },
  "1002": {
    skillName: "대지의 응보 (1회 추가시전)",
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
  "1003": {
    skillName: "대지가르기",
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
  "1004": {
    skillName: "번개일격",
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
  "1005": {
    skillName: "강철방어",
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
  "1006": {
    skillName: "대지의 응보 (1회 추가시전)",
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
  "1007": {
    skillName: "대지가르기",
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
  "1008": {
    skillName: "번개일격",
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
  "1009": {
    skillName: "강철방어",
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
  "10010": {
    skillName: "대지의 응보 (1회 추가시전)",
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
  "10011": {
    skillName: "대지가르기",
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
  "10012": {
    skillName: "번개일격",
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
  "10013": {
    skillName: "강철방어",
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
const MOCK_DATA = {
  contributors: [
    { id: 1, nickname: "나", server: 1001, job: "검성", isExecutor: true },
    { id: 2, nickname: "딜러A딜러A딜러A딜러A", server: 1002, job: "마도성" },
    { id: 3, nickname: "딜러B", server: 2001, job: "정령성" },
    { id: 4, nickname: "딜러C", server: 1003, job: "호법성" },
    { id: 5, nickname: "서폿A", server: 1004, job: "치유성" },
    { id: 6, nickname: "서폿B", server: 1005, job: "수호성" },
    { id: 7, nickname: "서2폿B", server: 1005, job: "수호성" },
    { id: 8, nickname: "서3폿B", server: 1005, job: "수호성" },
  ],
  battleStart: Date.now() - 93000,
  battleEnd: Date.now(),
  information: {
    "1": { amount: 4185000, dps: 125924, contribution: 35.5 },
    "2": { amount: 4185000, dps: 995642, contribution: 35.5 },
    "3": { amount: 2790000, dps: 72423, contribution: 20.1 },
    "4": { amount: 1395000, dps: 18954, contribution: 4.2 },
    "5": { amount: 279000, dps: 3000, contribution: 2.1 },
    "6": { amount: 139500, dps: 1500, contribution: 1.5 },
    "7": { amount: 139500, dps: 1500, contribution: 1.5 },
    "8": { amount: 139500, dps: 1500, contribution: 1.5 },
  },
  target: {
    id: 25166,
    mob: { code: 2980139, name: "증합체 바고트", boss: true },
    remainHp: 12345678,
  },
};

export const injectMockDpsData = () => {
  if ((window as any).javaBridge) return;

  (window as any).javaBridge = {
    getDpsData: () => JSON.stringify(MOCK_DATA),
    getBattleDetail: (_id: string) => JSON.stringify(MOCK_DETAIL_DATA),
    getBattleDetailFromList: (_idx: number, _uid: number) => JSON.stringify(MOCK_DETAIL_DATA),
    getBattleList: () => JSON.stringify(MOCK_HISTORY_DATA),
  };
};
