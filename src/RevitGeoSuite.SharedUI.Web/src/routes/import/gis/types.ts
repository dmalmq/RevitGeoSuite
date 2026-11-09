export type GisCategory = 'opening' | 'unit' | 'level' | 'detail'
export type GisFileAssignment = { path: string; levelId: number | null; category: GisCategory }
export type GisPhase = 'options' | 'running' | 'done' | 'failed'
