// Loopback simulcast SDP fix. In a browser↔browser loopback the answerer omits
// the `a=rid:`/`a=simulcast:` attributes, so the offerer drops all but one
// encoding and simulcast collapses to a single layer. Copy the offer's send
// rids into the answer as `recv` (plus the matching `a=simulcast:recv` line) so
// the offerer keeps every layer active. Idempotent: a no-op if the answer
// already carries rids.

export function mungeAnswerSimulcast(answerSdp: string, offerSdp: string): string {
    if (/^a=rid:/m.test(answerSdp))
        return answerSdp;

    const offerLines = offerSdp.split(/\r?\n/);
    const ridLines = offerLines
        .filter(l => l.startsWith('a=rid:'))
        .map(l => l.replace(/ send/, ' recv'));
    const simLine = offerLines.find(l => l.startsWith('a=simulcast:'));
    if (!ridLines.length || !simLine)
        return answerSdp;

    const simRecv = simLine.replace('send', 'recv');
    const lines = answerSdp.split(/\r?\n/);
    const vi = lines.findIndex(l => l.startsWith('m=video'));
    if (vi < 0)
        return answerSdp;

    let end = lines.findIndex((l, i) => i > vi && l.startsWith('m='));
    if (end < 0)
        end = lines.filter(l => l.length).length;

    lines.splice(end, 0, ...ridLines, simRecv);

    return lines.join('\r\n');
}
