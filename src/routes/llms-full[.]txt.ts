import { createFileRoute } from '@tanstack/react-router';

import { directoryText } from '@/lib/directory-text';

export const Route = createFileRoute('/llms-full.txt')({
  server: {
    handlers: {
      GET: () =>
        new Response(directoryText(true), {
          headers: { 'Content-Type': 'text/plain; charset=utf-8' },
        }),
    },
  },
});
