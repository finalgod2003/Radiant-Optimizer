import { createFileRoute } from '@tanstack/react-router';

import { directoryText } from '@/lib/directory-text';

export const Route = createFileRoute('/llms.txt')({
  server: {
    handlers: {
      GET: () =>
        new Response(directoryText(false), {
          headers: { 'Content-Type': 'text/plain; charset=utf-8' },
        }),
    },
  },
});
