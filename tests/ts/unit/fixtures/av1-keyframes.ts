// AV1 keyframes captured from three encoders, base64-encoded. The Chrome
// NVENC ones carry the defect `normalizeAv1FrameSize` repairs: their sequence
// header declares max_frame_size 1920x1088 whatever the configured resolution
// is. The two software encoders declare it correctly and must come through
// untouched.
//
// To regenerate: in each browser, configure a `VideoEncoder` for
// `av01.0.08M.08` at the size below with `latencyMode: 'realtime'` and the
// stated `hardwareAcceleration`, encode a few frames of any content with
// `keyFrame: true` on the first, and base64 the first chunk's bytes. The
// defect is GPU-specific, so the NVENC ones need a machine whose Chrome
// reports hardware AV1 encode.

export interface Av1Fixture {
    readonly encoder: string;
    readonly width: number;
    readonly height: number;
    readonly isKeyFrame: boolean;
    readonly base64: string;
}

export const nvenc320: Av1Fixture = {
    encoder: 'Chrome NVENC (hardware)',
    width: 320,
    height: 184,
    isKeyFrame: true,
    base64:
        'EgAKDAAAAEKrv8P+ABDMAjLCBRAACAJ+LeDv4Ifo1AgooooFAAQAwQBghFIHP16suPde5BvnqEnuWaMZBqAQwZVYMKhkLmJn'
        + 'imGV/mgOjePjO76VW89Q0A/ruYEHsr3MXd8B4nxcZnmsDu9K7jvZqwhXrdkJ6OpfSfFH5Iq/k9VNeuNuWMrlwg31SpcOCjbS'
        + 'kmhPdf4/LOGGRVQitS5pD/EOM5vcr8aT0d4+m3PevryoKdy5GjbyD5YECmGviRewGCcN9Zxui2oNdl5bvDr3hhxYdh6dzZ8j'
        + 'Ts7gOr2cYYui0fNOr6NuitKKiRq5iwgWkAjXU5jLx9eYLPz/VLiofYLm8rzEVuuCraTi+fddayTbN2jJ/Q6IWBqmy9FKIrfo'
        + 'UfPXqUfUpK1JFBSxeCHbz0X+kDwkHwhvzLPP9cYtEr5DNRS2cImUac/6qQKoLW2VsRonttQodGJ16B2nia3ADX8tXxxjIik2'
        + 'KKG1LLKv8TY7c71Um9/QnDMv9qh8RiPJdc6diwFVrdxNgE1XUZS7O9Xy9VJ9MIXQORq58ZpDlX1fAMctFjjgDAV4jw2CvFVh'
        + 'enHI/1vtEz+B3+0WAnjpMZlwFEpOOb7k+4RPcjdw5uD5St+LiW8f47kXEIJ5no81RiDE5ON/RZ1cHVrin+bvYdGkNJjP3CN/'
        + 'O6zoJo+R+j0VEiQosAa8pTU1/JVzW7MFm0ASKUIylcRMU3zqNSywNyMd3Bm4bEq8NTf6Uq3Tku9gaB0rOeNnwVe4ZBnoIuHf'
        + 'UKddYdsqX+Fc9HGwz12+/5hJ6Zs9ZNr6+AiQOXbVIA6YfYQl7LEpOw28+dNJ9i91g8fvqpWyhuzunCT4L+1yhxqnWGwA0iF4'
        + 'Et2prp/Rga1rnbCcEZjWs0w9bHR78+qMc/HxCFEobyUN98DH+uIG+75nK2V+sOt3UWLq5ph9PMrSsoX1hTCgK2cO7dSGYFTK'
        + '7zv2VYQ=',
};

export const nvenc640: Av1Fixture = {
    encoder: 'Chrome NVENC (hardware)',
    width: 640,
    height: 360,
    isKeyFrame: true,
    base64:
        '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',
};

export const nvenc640Delta: Av1Fixture = {
    encoder: 'Chrome NVENC (hardware)',
    width: 640,
    height: 360,
    isKeyFrame: false,
    base64:
        'EgAy4QUwAAwI/xAAEAAMAAgABQADAAHAAACfyzwd/BD9IjggooooFAAQAwYAhNtAuWIFzNLdexab9dnOuNmns2YfywBqI4sm'
        + 'CcT05yhITBzOzPIOnV9LHN9GTvBkT21M0K9jbsXJ8r864sSZwe9wSSD1O2OTW/BMnhRfiz1xih8LQ7yVIPvwpFSxvA2CueQ+'
        + 'CK1LiM2KoM1IkSonHX5LiWPH7u8/hBjKoMLAhBP483/j/5LRb20sBuYYXBKSYtbyHvaE5Rl5MoPzum0S16MWxt/oMhuKQ4Dc'
        + '28H4TPyqSiw1HS5/l7mToicRmI7Zx+BJyw1PyzdyIecl3e1Gx7vMN3U2V2wDpwFdSXtvB4IYhUxGiQ8sneycAP6O1VaAPC5f'
        + 'WIXWV3W9bWk2pczV+yYjDVAo3Fwv1dIPy4I/BV+xfCR2FqcSsLY2XlfNVzCbz33OMMZMpcFyPfBijXZMF2pY9vUmbcTP+16i'
        + '/x1bakQ58k+PunIJeJPyLDibXC9DA5BuUC87KjXIssuH8OEGZYH64kCQdBghMcHs1X6jxtROQJxIziVEq47iWq4ORkPA1dg5'
        + 'VMPSCVKQLfybmrNYybQataWYt/281plnDizHdjH9eFeRi5V7vlB6DA+FY4IUFA0Bb8h3qz1W1PlX2kt99OzzqELo+kQRqGtu'
        + 'E6q59vwbZNNvHdhQ834ZxJ54ksEfpcWuBnb5wnhodaERfJZDbQxF6en4aO/fFwctCYsZqqNYGggK2YzLp8JtVTTV4vnu0uSA'
        + 'snIyhIFRqAS61gqs4jhv3o8zgX0mOlQdBFrr7XG22n5oCFlL+Q6KaGcgi2X9TdNQK95dRgLceNtQ1mLF8nQcAEoIkMmnamDr'
        + 'skhGULXJUKqlKBuCngco2l09flLKu2SWbb1JgKSSRguzXGVBfswRXbXKLv07OR+YxgcG40bbFAVPQ/J4yUQS4wVT7gOnewrD'
        + 'feQklAe/XXil8itDuiXxtghBggZR0A==',
};

export const nvenc1280: Av1Fixture = {
    encoder: 'Chrome NVENC (hardware)',
    width: 1280,
    height: 720,
    isKeyFrame: true,
    base64:
        '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'
        + 'i+/twA==',
};

export const nvenc480: Av1Fixture = {
    encoder: 'Chrome NVENC (hardware)',
    width: 480,
    height: 272,
    isKeyFrame: true,
    base64:
        'EgAKDAAAAEarv8P+ABDMAjJlEAAIA75D4O/gh+gkCAggggEABADBABJyMw8cc2azTwWoHBJ+bF2lwd58pvEgZabrcj4nFfbO'
        + 'zPujKbaJUIkmNfYBw9t2pfES7OrEtLP8iShcq405z3GN7iSYK1+dOZSLxsivZcg=',
};

export const nvenc960: Av1Fixture = {
    encoder: 'Chrome NVENC (hardware)',
    width: 960,
    height: 544,
    isKeyFrame: true,
    base64:
        'EgAKDAAAAEarv8P+ABDMAjKRARAACAd+h+Dv4IfopAgccccFAAQAwQASclVsQp/CYnvJNj9mp+FyTc3xTLYED6pU9Pd4uakG'
        + 'e70QjZS0sO5CtDk+lkXN2TQp8xuo0E93mWPjuxAvlL0cXm/piYvWWecJxHv7erMduW3Opuwbt2fs2JSZIkxwgQ4bm9wG0KdO'
        + 'p7USENizKKgNndecPN3xS6YOsIg=',
};

export const libaom480: Av1Fixture = {
    encoder: 'Chrome libaom (software)',
    width: 480,
    height: 272,
    isKeyFrame: true,
    base64:
        'EgAKDQAAAARHfh4A0QEBAQQyqwMQQ8ADDDDEAgkAtE7m4DSR8tKzAbuB8fnApnjC8pHcjVq8Bk/QY6ThG/zP//+Zi/Dw8OZG'
        + 'nInCoZECY1niWEA4UKeDlb7b7TvbDcET9s3qsAkGVNGz0wb84HmYmct+Qs9QTQC+x4tk4Uu0K+tja7OPd9WfqvbdrX7kma5U'
        + 'pvaJh0ZHmN6vSW7p/S6tn7fAboAHBUIFlMNmXWmVx6lFCaxVsQHMoOCPnA9A9nI0f66P0/pThlppdEUpTnlAP/EEd9Q1mj2c'
        + 'oYGWG8hXrxlTCGlvhOyBxKqBF3UO46X5Twn/brRTj+4qws7EQl1PVCQfLknJ56W0c9mkmkCDKFWWsqEfqqSnqDC3DiTnRoST'
        + '6ed8zA9fSFWFlX+In+jCqNTw0LAjTcNkhc4xdlOh0V+VarrcnFwRDKi1VS2M2VudjC9RA2rcyfpxvaC6LyVBdoxFMA682PYv'
        + '1AcNdyjxG0y5z0OtMPsMuh2Ddh+0rz9sqsTVyxzh5Nz94a+zTXnpeUp7dxr0Ukj6U1lSv9mxc212LQ/2ChTP0OJ7rtNEYT5a'
        + 'byVdZzol9EhwDxrvDVBA',
};

export const libaom960: Av1Fixture = {
    encoder: 'Chrome libaom (software)',
    width: 960,
    height: 544,
    isKeyFrame: true,
    base64:
        'EgAKDQAAACTPfw+ANEBAQEEyygQQYZkBBBBAUkmAAQAALAG1H518SSseFYSozaiUHwjqpTAkasp5ahciDSxmf7oh2+tAdMP2'
        + 'XhJde9G+A9LwUnvVXpJ/uF9E/e6+cP3HLTuaT3txlpKV5u4/qFeE28uvEtmvmSu5uV7UDPNkVbAKrkaC/k9BpRX6ND2nE2Ee'
        + 'olniWe0hml99dmHh3Y2oKQa6SNCwQL8B7XU/oqcKSnUB3HoBA8x68zE8H0q6UFz/qtttb+MbztyVq9rIvSB+3V5MLW7bXVqY'
        + 'AiRENF6yYolHLpNuw8o6kXBKCXB6FeflTQSgd1ChvOVPq0RdkRtZ2xqLPs3181mU/qUPYZRh+JM5JF6dR1cj3U/sK2BCdjFy'
        + '/XeXF/WN/uzQAsvRjVM2vEbrz9I10o+isvGG9DugAMoRO/jT2LvTnfM4xwSeF/7EtR+dfEkrHhWEqM2nxceqpb1CKot3xFtp'
        + 'MNTnuwxqs3EUwNw0VI91LFALW2SY38jJcG+TUv7dRVxyaLRI+2B2YuxOS0nNnSSe9pvT8ZDDWRG3M0rLvhl6vVcxz8xP2L7K'
        + 'XKMHMFZuwE8U1xcncdesam6V6I3mwgO4J69xLq6cXfvxLM0WUBwepbSU2Kn8HYkrOz9iB89QMYrQc6PfawPGBGYxThq1j0TX'
        + '1lqYn9RIugBhRxSr+hYzc6X+8a3IlrlFEjwCUsiuzmKQyAbtKcfOZl40FDwy9GbxN+uf+uBo2zs/uuBLjMvF+ME+0vSR6FDU'
        + 'OolR1RG86my+/StfLIs64SB2RC8XbJWOwjzM4BqA',
};

export const firefox320: Av1Fixture = {
    encoder: 'Firefox libaom (software)',
    width: 320,
    height: 184,
    isKeyFrame: true,
    base64:
        'EgAKDAAAAAQ8/t+BtfIAgDLyAhCUkAEKAAggghABZLRNcf+6E60wlzg9mCsHR6e9j2sUg//kNImgYNMx3saCkCr+Sh1jbQn9'
        + '7ZsDqdIXHQFDQCH97PuaPt++IFXH+Sz0yzjdP8lWoKYMNhbMosTrcH23SmjSwqAyu0gtW0G025+rP8e8MP2HwtQUvq97I9We'
        + 'QWIwkkvQJxXOhLlfxI7BRSz/dEjjlyefvbYosUNNOn0yYrw8R6pZKd/d4UJjqh37xKfnDuMnLbQBqPCSOXDZLWaIo7oLNhoE'
        + 'CQFhtzrM9009AmLTdwrirS56oitJVDCW7Ydz7aBnqqt4igbCsRFKNq63z0Ec93J1/Y2TUV+DzjLZk3roF/mWUC5UO/oYhmMA'
        + 'w6G+J+NttlsigbR4G+lrR+cisvykKaF51o+fEE62hklpXePl9A4N2JCP/bPV/bkAEfyM9fZaxokwc4D8+Tel9hvlgj/YiJtx'
        + '1uL0oDRkFQCWXyYoD28hvffmyzQrlGNEFnB4fqs=',
};

export const firefox640: Av1Fixture = {
    encoder: 'Firefox libaom (software)',
    width: 640,
    height: 360,
    isKeyFrame: true,
    base64:
        'EgAKDAAAAAzE/2fgbXyAIDLgAhCUkAENQAggghABdLRNcdWso6v5mrgu5Os4uWRbuEW8qpJrm/fjmBlthU+K5BJ2G8iRuSBf'
        + '7xDMG84QLH/2jPhoiL6k9x0lF95B/2YIXagLEofqe3wD8Xkpjt720o0UbYiKWB+ByT5awB7KAnUNui47Bgup/kjrUWF4kkjH'
        + 'jmwGKoXWUUYeyLmvvrlGwo9fKa08FcTObFyBiuOtBMA50Ag+AaF95tP6rrErwDinvU6btTYfjCOZGzAkaYB7QBycHaTmSbl6'
        + 'OzGRgQurIK3AEURZ5yeYNvg7+zPnh7qn9eMAtX2QfBexwyWRj0m+ue3ERcPRXtdS7xySAYSY2JenzYIz/pAsiLWl1/iMDCKX'
        + 'nKNj+C4w535DL3K0wp8+tdHgtuWsbV/wNZwI9kGOOSYVbGAaSKBGblYf5zK4nSFbGH345isG2VJGUpiebGC5NgB0SHGw9XJO'
        + '6pXx7aBWcFhb15M=',
};

export const nvencKeyFrames: readonly Av1Fixture[] = [nvenc320, nvenc640, nvenc1280, nvenc480, nvenc960];
export const softwareKeyFrames: readonly Av1Fixture[] = [libaom480, libaom960, firefox320, firefox640];
