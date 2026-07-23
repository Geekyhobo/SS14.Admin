window.ss14MobileNav = {
    lockBodyScroll() {
        const body = document.body;
        if (!body) return;

        if (!body.dataset.prevOverflow) {
            body.dataset.prevOverflow = body.style.overflow || '';
        }

        body.style.overflow = 'hidden';
    },

    unlockBodyScroll() {
        const body = document.body;
        if (!body) return;

        body.style.overflow = body.dataset.prevOverflow || '';
        delete body.dataset.prevOverflow;
    },

    focusElement(element) {
        if (element && typeof element.focus === 'function') {
            requestAnimationFrame(() => element.focus());
        }
    }
};
