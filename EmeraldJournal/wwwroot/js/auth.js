window.postJson = async function postJson(url, data) {
    const resp = await fetch(url, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        credentials: "include",   
        body: JSON.stringify(data)
    });
    return { ok: resp.ok, text: await resp.text() };
};