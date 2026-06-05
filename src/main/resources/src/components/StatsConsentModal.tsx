import { useState } from "react";
import { BarChart3, ShieldCheck, UsersRound } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Switch } from "@/components/ui/switch";

export interface StatsOwnCharacter {
  detected: boolean;
  id?: number;
  nickname?: string;
  server?: number;
  job?: string;
  power?: number;
}

interface Props {
  open: boolean;
  character: StatsOwnCharacter | null;
  onAccept: (publicCharacter: boolean) => void;
  onDecline: () => void;
}

export function StatsConsentModal({ open, character, onAccept, onDecline }: Props) {
  const [publicCharacter, setPublicCharacter] = useState(true);
  const label = character?.nickname
    ? `${character.nickname}${character.job ? ` · ${character.job}` : ""}`
    : "내 캐릭터";

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (!next) onDecline();
      }}>
      <DialogContent
        showCloseButton={false}
        className="max-w-[420px] border-white/10 bg-[#08111f]/95 text-slate-50 shadow-2xl">
        <DialogHeader>
          <div className="mb-1 flex size-10 items-center justify-center rounded-md border border-cyan-300/20 bg-cyan-300/10 text-cyan-200">
            <BarChart3 className="size-5" />
          </div>
          <DialogTitle className="text-lg font-bold">전투 통계 수집 동의</DialogTitle>
          <DialogDescription className="text-sm leading-6 text-slate-300">
            {label} 기준으로 보스를 처치해 끝난 전투 요약만 웹 통계에 사용할 수 있습니다.
          </DialogDescription>
        </DialogHeader>

        <div className="grid gap-2.5 text-sm">
          <div className="flex gap-3 rounded-md border border-white/10 bg-white/[0.04] p-3">
            <ShieldCheck className="mt-0.5 size-4 shrink-0 text-emerald-300" />
            <p className="leading-5 text-slate-200">
              원본 패킷은 전송하지 않고, 본인 캐릭터의 전투 요약과 스킬/버프 지표만 전송합니다.
            </p>
          </div>
          <div className="flex gap-3 rounded-md border border-white/10 bg-white/[0.04] p-3">
            <UsersRound className="mt-0.5 size-4 shrink-0 text-cyan-300" />
            <p className="leading-5 text-slate-200">
              파티원은 닉네임 없이 직업 구성과 시너지 수만 사용합니다.
            </p>
          </div>
          <label className="flex items-center justify-between gap-4 rounded-md border border-white/10 bg-white/[0.04] p-3">
            <span>
              <span className="block font-bold text-slate-100">내 캐릭터 통계 공개</span>
              <span className="text-xs text-slate-400">끄면 비공개 업로드만 허용됩니다.</span>
            </span>
            <Switch
              checked={publicCharacter}
              onCheckedChange={setPublicCharacter}
              className="data-[state=checked]:bg-emerald-500"
            />
          </label>
        </div>

        <DialogFooter className="-mx-4 -mb-4 border-white/10 bg-white/[0.03]">
          <Button
            variant="ghost"
            onClick={onDecline}
            className="text-slate-300 hover:bg-white/10 hover:text-white">
            동의하지 않음
          </Button>
          <Button
            onClick={() => onAccept(publicCharacter)}
            className="bg-cyan-600 text-white hover:bg-cyan-500">
            동의하고 자동 업로드
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
