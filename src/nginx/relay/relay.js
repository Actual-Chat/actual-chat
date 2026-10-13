import cr from 'crypto';

const Version = 'v1';

function env(name) {
    const value = process.env[name];
    if (!value)
        throw new Error(`${name} is not set`);
    return value;
}

// Where the relay forwards RPC traffic, as host:port
function origin(s) {
    return env('RELAY_ORIGIN');
}

// v1|<relay id>|<client ip>|<unix seconds>|<signature>
function attestation(s) {
    const payload = [Version, env('RELAY_ID'), s.remoteAddress, Math.floor(Date.now() / 1000)].join('|');
    const signature = cr.createHmac('sha256', env('RELAY_KEY')).update(payload).digest('base64url');
    return `${payload}|${signature}\n`;
}

export default { origin, attestation };
