import { dotnet } from './desktop-runtime.mjs';

dotnet([
  'run',
  '--project',
  'desktop/Radiant.Tests/Radiant.Tests.csproj',
  '-c',
  'Release',
]);

for (const project of ['Radiant.Games.Tests', 'Radiant.Monitoring.Tests']) {
  dotnet([
    'run',
    '--project',
    `desktop/${project}/${project}.csproj`,
    '-c',
    'Release',
  ]);
}
dotnet([
  'run',
  '--project',
  'desktop/Radiant.Licensing.Tests/Radiant.Licensing.Tests.csproj',
  '-c',
  'Release',
]);
