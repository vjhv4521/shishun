// Local test double, NOT DeepSeek. Used only to verify the HTTP/Unity success path without API credentials.
import http from 'node:http';

if (!process.argv.includes('--test-fixture')) throw new Error('Explicit --test-fixture is required.');
const server = http.createServer(async (request, response) => {
    try {
        let body = '';
        for await (const chunk of request) {
            body += chunk.toString('utf8');
            if (body.length > 65536) throw new Error('Request too large');
        }
        const payload = JSON.parse(body);
        if (request.url !== '/chat/completions' || payload.thinking?.type !== 'disabled' ||
            payload.response_format?.type !== 'json_object') throw new Error('Unexpected model request');
        const context = JSON.parse(payload.messages.at(-1).content);
        let content;
        if (Array.isArray(context.candidates)) {
            const candidate = context.candidates[0];
            if (!candidate?.id) throw new Error('Missing candidate');
            content = JSON.stringify({ candidateId: candidate.id, dialogue: '营地的储备还不充足，帮大家备些物资吧。彼此照应，才能在荒野里安稳住下。' });
        } else {
            if (!context.playerMessage || !context.context?.sessionId) throw new Error('Missing chat context');
            content = JSON.stringify({ reply: context.context.hasFirepit
                ? '火已经生起来了，木料还要留一些作夜里的储备。'
                : '营地还缺篝火，先备些石料；物资交付后仍要亲手搭建。' });
        }
        response.writeHead(200, { 'Content-Type': 'application/json' });
        response.end(JSON.stringify({ choices: [{ message: { role: 'assistant', content } }] }));
        console.log(`[MODEL_FIXTURE_ONLY] requestId=${context.requestId} kind=${Array.isArray(context.candidates) ? 'quest' : 'chat'}`);
    } catch {
        response.writeHead(400, { 'Content-Type': 'application/json' });
        response.end(JSON.stringify({ error: 'invalid_fixture_request' }));
    }
});
server.listen(5091, '127.0.0.1', () => console.log('[MODEL_FIXTURE_ONLY] http://127.0.0.1:5091 — no external model is called'));
