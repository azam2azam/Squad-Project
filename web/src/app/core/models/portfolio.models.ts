/**
 * The portfolio roll-up served by /portfolio/summary. Shared by the dashboard charts and
 * the command-centre headline strip so the two can never disagree about a number.
 */
export interface PortfolioSummary {
  headline: {
    totalBoards: number;
    totalPeople: number;
    averageProgressPercent: number;
    onTrackPercent: number;
    squadCount: number;
    boardsNeedingAttention: number;
  };
  statusBreakdown: {
    status: number;
    label: string;
    color: string;
    count: number;
    percent: number;
    needsTexture: boolean;
  }[];
  squads: {
    squadName: string;
    boardCount: number;
    memberCount: number;
    averageProgressPercent: number;
    onTrackCount: number;
    atRiskCount: number;
    blockedCount: number;
    deliveredCount: number;
  }[];
  riskRegister: {
    boardId: string;
    title: string;
    squadName: string;
    level: number;
    levelLabel: string;
    levelColor: string;
    riskNote: string | null;
    status: number;
    statusLabel: string;
    progressPercent: number;
  }[];
  roleTotals: { role: number; label: string; color: string; count: number }[];
  needsAttention: {
    boardId: string;
    title: string;
    squadName: string;
    reasons: string[];
  }[];
}
