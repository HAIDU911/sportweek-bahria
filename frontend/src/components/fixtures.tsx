import { MapPin } from 'lucide-react';
import { fmtTime, roundLabel, scoreText } from '../lib/format';
import type { FeedFixture } from '../lib/types';
import { cx, StatusBadge } from './ui';

export function ScoreCard({ f }: { f: FeedFixture }) {
  const [h, a] = scoreText(f);
  const live = f.status === 'Live';
  return (
    <article className="sc" aria-label={`${f.homeTeam ?? 'TBD'} versus ${f.awayTeam ?? 'TBD'}`}>
      <div className="sc-top">
        <StatusBadge status={f.status} /><span>{f.sportName} · {f.isThirdPlace ? 'Third place' : `Round ${f.round}`}</span>
        <span>{live && f.liveClock ? f.liveClock : fmtTime(f.startsAtUtc)}</span>
      </div>
      <div className="sc-body">
        <div className={cx('sc-team', f.winnerTeam && f.winnerTeam === f.homeTeam && 'win')}><div className="dept">{f.homeDepartment ?? ''}</div><div className="name">{f.homeTeam ?? 'TBD'}</div></div>
        <div className="sc-score" aria-label={`Score ${h} to ${a}`}><span>{h}</span><span className="colon">:</span><span>{a}</span></div>
        <div className={cx('sc-team r', f.winnerTeam && f.winnerTeam === f.awayTeam && 'win')}><div className="dept">{f.awayDepartment ?? ''}</div><div className="name">{f.awayTeam ?? 'TBD'}</div></div>
      </div>
      <div className="sc-foot"><span><MapPin size={11} style={{ verticalAlign: -1 }} aria-hidden /> {f.venue ?? 'Venue TBC'}</span><span>{live ? f.livePeriod ?? 'Live' : f.tournamentName}</span></div>
    </article>
  );
}

export function FixtureRow({ f, maxRound }: { f: FeedFixture; maxRound?: number }) {
  const [h, a] = scoreText(f);
  return (
    <div className="frow">
      <span className="t">{fmtTime(f.startsAtUtc)}</span>
      <span className={cx('tm', f.winnerTeam && f.winnerTeam === f.homeTeam && 'win')}>{f.homeTeam ?? 'TBD'}</span>
      <span className="sc2">{h} : {a}</span>
      <span className={cx('tm r', f.winnerTeam && f.winnerTeam === f.awayTeam && 'win')}>{f.awayTeam ?? 'TBD'}</span>
      <StatusBadge status={f.status} />
      <span className="meta">{f.sportName} · {maxRound ? roundLabel(f.round, maxRound, f.isThirdPlace) : `Round ${f.round}`} · {f.venue ?? 'Venue TBC'}</span>
    </div>
  );
}

/** Single-elimination bracket built from the public fixture feed (byes are not listed — those teams appear directly in round 2). */
export function Bracket({ fixtures }: { fixtures: FeedFixture[] }) {
  const main = fixtures.filter((f) => !f.isThirdPlace);
  const third = fixtures.find((f) => f.isThirdPlace);
  const rounds = [...new Set(main.map((f) => f.round))].sort((a, b) => a - b);
  const maxRound = rounds[rounds.length - 1] ?? 1;
  if (!rounds.length) return null;
  return (
    <div>
      <div className="bracket" role="group" aria-label="Tournament bracket">
        {rounds.map((r) => (
          <div className="bcol" key={r}>
            <h4>{roundLabel(r, maxRound, false)}</h4>
            <div className="bmatches">{main.filter((f) => f.round === r).sort((a, b) => a.slot - b.slot).map((f) => <BNode key={f.id} f={f} />)}</div>
          </div>
        ))}
      </div>
      {third && <div style={{ maxWidth: 260, marginTop: 8 }}><h4 className="eyebrow" style={{ marginBottom: 8 }}>Third place</h4><BNode f={third} /></div>}
    </div>
  );
}

function BNode({ f }: { f: FeedFixture }) {
  const [h, a] = scoreText(f);
  return (
    <div className="bnode">
      <div className={cx('bt', f.winnerTeam && f.winnerTeam === f.homeTeam && 'win', !f.homeTeam && 'tbd')}><span>{f.homeTeam ?? 'To be decided'}</span><b>{h}</b></div>
      <div className={cx('bt', f.winnerTeam && f.winnerTeam === f.awayTeam && 'win', !f.awayTeam && 'tbd')}><span>{f.awayTeam ?? 'To be decided'}</span><b>{a}</b></div>
      <div className="bm"><span>{f.status}</span><span>{fmtTime(f.startsAtUtc)}</span></div>
    </div>
  );
}
