// The survey point question in Export to Cesium. The model remembers the answer as 'TP' or
// 'notTp'; anything else (a model never asked, an old 'unknown') leaves the question unanswered.

export type DatumAnswer = '' | 'TP' | 'notTp'

export function datumAnswer(lastHeightDatum: string | null | undefined): DatumAnswer {
  return lastHeightDatum === 'TP' || lastHeightDatum === 'notTp' ? lastHeightDatum : ''
}
