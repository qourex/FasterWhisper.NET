import { defineConfig } from 'vitepress'

export default defineConfig({
  base: '/FasterWhisper.NET/',
  title: 'FasterWhisper.NET',
  description: 'High-Performance Offline Speech-to-Text for .NET',
  cleanUrls: true,
  lastUpdated: true,
  sitemap: {
    hostname: 'https://qourex.github.io/FasterWhisper.NET/'
  },
  head: [
    ['link', { rel: 'icon', type: 'image/png', href: 'https://raw.githubusercontent.com/qourex/FasterWhisper.NET/main/src/Qourex.FasterWhisper.NET/icon.png' }],
    ['meta', { name: 'author', content: 'Qourex' }],
    ['meta', { name: 'keywords', content: 'whisper, speech-to-text, dotnet, csharp, ctranslate2, faster-whisper, speech-recognition, android, ios, maui, winforms, aspnetcore, blazor' }],
    ['meta', { property: 'og:title', content: 'FasterWhisper.NET' }],
    ['meta', { property: 'og:description', content: 'High-performance C# SDK wrapping CTranslate2 for offline Whisper audio transcription in .NET.' }],
    ['meta', { property: 'og:image', content: 'https://raw.githubusercontent.com/qourex/FasterWhisper.NET/main/social-card.png' }],
    ['meta', { property: 'og:type', content: 'website' }],
    ['meta', { name: 'twitter:card', content: 'summary_large_image' }],
    ['meta', { name: 'twitter:title', content: 'FasterWhisper.NET' }],
    ['meta', { name: 'twitter:description', content: 'High-performance C# SDK wrapping CTranslate2 for offline Whisper audio transcription in .NET.' }],
    ['meta', { name: 'twitter:image', content: 'https://raw.githubusercontent.com/qourex/FasterWhisper.NET/main/social-card.png' }],
    [
      'script',
      { type: 'application/ld+json' },
      JSON.stringify({
        '@context': 'https://schema.org',
        '@type': 'SoftwareApplication',
        'name': 'FasterWhisper.NET',
        'operatingSystem': 'Windows, Linux, macOS, Android, iOS',
        'applicationCategory': 'DeveloperApplication',
        'description': 'High-performance C# SDK wrapping CTranslate2 for offline speech-to-text audio transcription in .NET.',
        'offers': {
          '@type': 'Offer',
          'price': '0',
          'priceCurrency': 'USD'
        },
        'author': {
          '@type': 'Organization',
          'name': 'Qourex',
          'url': 'https://qourex.com'
        },
        'downloadUrl': 'https://www.nuget.org/packages/FasterWhisper.NET',
        'softwareVersion': '1.0.2',
        'license': 'https://opensource.org/licenses/MIT'
      })
    ]
  ],
  themeConfig: {
    logo: 'https://raw.githubusercontent.com/qourex/FasterWhisper.NET/main/src/Qourex.FasterWhisper.NET/icon.png',
    nav: [
      { text: 'Guide', link: '/guide/getting-started' },
      { text: 'Advanced', link: '/guide/advanced-features' },
      { text: 'API Reference', link: '/guide/api-reference' },
      { text: 'Samples', link: '/guide/samples' },
      { text: 'Mobile Deployment', link: '/guide/mobile-deployment' },
      { text: 'NuGet', link: 'https://www.nuget.org/packages/FasterWhisper.NET' }
    ],
    sidebar: [
      {
        text: 'Introduction',
        items: [
          { text: 'Getting Started', link: '/guide/getting-started' },
          { text: 'Available Models', link: '/guide/getting-started#available-models' }
        ]
      },
      {
        text: 'Features & API',
        items: [
          { text: 'Advanced Usage', link: '/guide/advanced-features' },
          { text: 'API Reference', link: '/guide/api-reference' }
        ]
      },
      {
        text: 'Samples & Demos',
        items: [
          { text: 'The .NET 10.0 Suite', link: '/guide/samples' }
        ]
      },
      {
        text: 'Platform Support',
        items: [
          { text: 'Mobile Deployment (Android & iOS)', link: '/guide/mobile-deployment' }
        ]
      }
    ],
    socialLinks: [
      { icon: 'github', link: 'https://github.com/qourex/FasterWhisper.NET' }
    ],
    footer: {
      message: 'Released under the MIT License.',
      copyright: 'Copyright © 2026 Qourex'
    },
    search: {
      provider: 'local'
    }
  }
})
