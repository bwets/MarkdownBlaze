// The two elements every module that lays the document out is concerned with.
export function pageParts() {
    const flow = document.querySelector('.content.markdown-body');
    const pane = flow && flow.closest('.content-area');
    return { flow, pane };
}
