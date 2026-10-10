import { createFileRoute } from '@tanstack/react-router';

import { envConfigs } from '@/config';
import { m } from '@/paraglide/messages.js';
import { getLocale } from '@/paraglide/runtime.js';
import { OptimizerDownload } from '@/blocks/optimizer/download';
import { optimizerMetadata } from '@/blocks/optimizer/metadata';

export const Route = createFileRoute('/download')({
  loader: () => {
    const locale = getLocale();
    return {
      locale,
      title: m['optimizer.meta.download_title'](
        { name: envConfigs.app_name },
        { locale }
      ),
      description: m['optimizer.meta.download_description']({}, { locale }),
    };
  },
  head: ({ loaderData }) => optimizerMetadata(loaderData, '/download'),
  component: OptimizerDownload,
});
