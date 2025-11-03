const connection = new signalR.HubConnectionBuilder()
    .withUrl("/loghub")
    .build();

connection.on("ReceiveLog", (computer, step, content) => {
    const logBox = document.getElementById("logBox");
    const msg = `[${computer}] STEP ${step}\n${content}\n\n`;
    logBox.textContent += msg;
    logBox.scrollTop = logBox.scrollHeight;
});

connection.start().catch(err => console.error(err.toString()));
