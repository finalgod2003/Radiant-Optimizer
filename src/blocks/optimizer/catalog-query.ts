import { useQuery } from '@tanstack/react-query';

import { apiGet } from '@/lib/api-client';
import type { OptimizerCatalog } from '@/lib/optimizer-contracts';

export function useOptimizerCatalog() {
  return useQuery({
    queryKey: ['optimizer-catalog'],
    queryFn: () => apiGet<OptimizerCatalog>('/api/optimizer/catalog'),
    staleTime: 60_000,
    retry: 1,
  });
}
