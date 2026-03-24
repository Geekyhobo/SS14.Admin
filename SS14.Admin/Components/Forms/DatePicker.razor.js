export class DatePicker {
    static init(ref, id, options)
    {
        return new DatePicker(ref, id, options);
    }

    constructor(ref, id, options)
    {
        this.ref = ref;
        this.id = id;
        this.root = document.getElementById(id);
        this.currentMonthButton = this.root.querySelector('.current-month');
        this.daysContainer = this.root.querySelector('.days-container');
        this.popup = this.root.querySelector('.datepicker-container');
        this.popupButton = this.root.querySelector('.datepicker-toggle');
        this.displayInput = this.root.querySelector('.date-display');

        this.popupButton.addEventListener('click', () =>
        {
            if (this.popup.classList.contains('hidden'))
            {
                this.showPopup()
            }
            else
            {
                this.hidePopup()
            }
        });

        this.root.querySelector('.cancel-button').addEventListener('click', () => this.hidePopup());
        this.root.querySelector('.ok-button').addEventListener('click', () => this.confirmDates());
        this.root.querySelector('.clear-button').addEventListener('click', () => this.clearDates());
        this.root.querySelector('.prev').addEventListener('click', () => this.prevMonth());
        this.root.querySelector('.next').addEventListener('click', () => this.nextMonth());

        this.fromDateInput = this.root.querySelector('.from-date-input');
        this.fromDateInput.addEventListener('change', () => this.setFromDate());
        this.fromTimeInput = this.root.querySelector('.from-time-input');
        this.fromTimeInput.addEventListener('change', () => this.setFromDate());
        this.toDateInput = this.root.querySelector('.to-date-input');
        this.toDateInput.addEventListener('change', () => this.setToDate());
        this.toTimeInput = this.root.querySelector('.to-time-input');
        this.toTimeInput.addEventListener('change', () => this.setToDate());

        this.daysContainer.addEventListener('click', (e) => this.onDayClick(e));

        this.updatePosition();

        document.addEventListener('scroll', () => this.updatePosition(),{
            capture: true,
            passive: true
        });

        window.addEventListener('resize', () => this.updatePosition(), { capture: true });

        this.rangeStart = options.rangeStart != null ? dayjs(options.rangeStart) : null;
        this.rangeEnd = options.rangeEnd != null ? dayjs(options.rangeEnd) : null;

        // Set input values from initial state
        if (this.rangeStart && !this.isNanOrNull(this.rangeStart)) {
            this.fromDateInput.value = this.rangeStart.format('YYYY-MM-DD');
        }
        if (this.rangeEnd && !this.isNanOrNull(this.rangeEnd)) {
            this.toDateInput.value = this.rangeEnd.format('YYYY-MM-DD');
        }

        this.currentDisplayDate = this.rangeStart != null && !this.isNanOrNull(this.rangeStart) ? this.rangeStart : dayjs();
        this.updateDisplay();
        this.updateDisplayInput();
    }

    onDayClick(e)
    {
        const dayEl = e.target.closest('.day');
        if (!dayEl) return;

        const dateStr = dayEl.dataset.day;
        if (!dateStr) return;

        const clicked = dayjs(dateStr);

        // If no start selected, or both are selected, start fresh
        if (this.isNanOrNull(this.rangeStart) || (!this.isNanOrNull(this.rangeStart) && !this.isNanOrNull(this.rangeEnd))) {
            this.rangeStart = clicked;
            this.rangeEnd = null;
            this.fromDateInput.value = clicked.format('YYYY-MM-DD');
            this.toDateInput.value = '';
        }
        // If start is selected but not end
        else {
            if (clicked.isBefore(this.rangeStart, 'day')) {
                // Clicked before start — swap
                this.rangeEnd = this.rangeStart;
                this.rangeStart = clicked;
                this.fromDateInput.value = clicked.format('YYYY-MM-DD');
                this.toDateInput.value = this.rangeEnd.format('YYYY-MM-DD');
            } else {
                this.rangeEnd = clicked;
                this.toDateInput.value = clicked.format('YYYY-MM-DD');
            }
        }

        this.updateDisplay();
    }

    confirmDates()
    {
        let fromDateTime = null;
        let toDateTime = null;

        if (!this.isNanOrNull(this.rangeStart)) {
            const fromTime = this.fromTimeInput.value;
            fromDateTime = this.rangeStart.format('YYYY-MM-DD');
            if (fromTime) {
                fromDateTime += 'T' + fromTime;
            }
        }

        if (!this.isNanOrNull(this.rangeEnd)) {
            const toTime = this.toTimeInput.value;
            toDateTime = this.rangeEnd.format('YYYY-MM-DD');
            if (toTime) {
                toDateTime += 'T' + toTime;
            }
        }

        this.ref.invokeMethodAsync('OnDatesConfirmed', fromDateTime, toDateTime);
        this.updateDisplayInput();
        this.hidePopup();
    }

    clearDates()
    {
        this.rangeStart = null;
        this.rangeEnd = null;
        this.fromDateInput.value = '';
        this.fromTimeInput.value = '';
        this.toDateInput.value = '';
        this.toTimeInput.value = '';
        this.ref.invokeMethodAsync('OnDatesConfirmed', null, null);
        this.updateDisplay();
        this.updateDisplayInput();
        this.hidePopup();
    }

    updateDisplayInput()
    {
        if (!this.displayInput) return;

        if (!this.isNanOrNull(this.rangeStart) && !this.isNanOrNull(this.rangeEnd)) {
            this.displayInput.value = this.rangeStart.format('YYYY-MM-DD') + '  —  ' + this.rangeEnd.format('YYYY-MM-DD');
        } else if (!this.isNanOrNull(this.rangeStart)) {
            this.displayInput.value = this.rangeStart.format('YYYY-MM-DD') + '  —  ...';
        } else {
            this.displayInput.value = '';
        }
    }

    showPopup()
    {
        this.updateDisplay()
        this.popup.classList.remove('hidden');
        this.updatePosition()
    }

    hidePopup()
    {
        this.popup.classList.add('hidden');
    }

    updatePosition()
    {
        if (this.popup.classList.contains('hidden'))
            return;

        FloatingUIDOM.computePosition(this.popupButton, this.popup, {
            placement: 'bottom',
            middleware: [
                FloatingUIDOM.offset(6),
                FloatingUIDOM.flip(),
                FloatingUIDOM.shift({padding: 5})
            ]
        }).then(({x, y, placement, middlewareData}) => {
            Object.assign(this.popup.style, {
                left: `${x}px`,
                top: `${y}px`
            });
        });
    }

    updateDisplay()
    {
        this.daysContainer.innerHTML = "";
        this.currentMonthButton.textContent = this.currentDisplayDate.toDate().toLocaleString('en-us', {month: 'long', year: 'numeric'});
        let numberOfDays = DatePicker.getDaysInMonth(this.currentDisplayDate.toDate());

        for (let i = 0; i < numberOfDays; i++)
        {
            const day = this.currentDisplayDate.date(i + 1);
            const isSelected = (!this.isNanOrNull(this.rangeStart) && day.isSameOrAfter(this.rangeStart, 'day'))
                                    && (!this.isNanOrNull(this.rangeEnd) && day.isSameOrBefore(this.rangeEnd, 'day'));
            const isStart = !this.isNanOrNull(this.rangeStart) && day.isSame(this.rangeStart, 'day');
            const isEnd = !this.isNanOrNull(this.rangeEnd) && day.isSame(this.rangeEnd, 'day');

            let classes = 'day w-6 h-6 day-text-nudge text-center text-sm cursor-pointer rounded';
            if (isSelected) classes += ' day-selected';
            else if (isStart || isEnd) classes += ' day-selected';

            this.daysContainer.innerHTML += `<div data-day="${day.format('YYYY-MM-DD')}" class="${classes}">${i + 1}</div>`;
        }
    }

    static getDaysInMonth(date)
    {
        return new Date(date.getFullYear(), date.getMonth() + 1, 0).getDate();
    }

    prevMonth() {
        this.currentDisplayDate = this.currentDisplayDate.subtract(1, 'month');
        this.updateDisplay();
    }

    nextMonth() {
        this.currentDisplayDate = this.currentDisplayDate.add(1, 'month');
        this.updateDisplay();
    }

    setFromDate()
    {
        let date = dayjs(this.fromDateInput.valueAsDate).utc().hour(0);
        const time = this.fromTimeInput.valueAsNumber;

        if (date != null && time != null && !isNaN(time))
            date = date.millisecond(time);

        this.rangeStart = date;
        this.updateDisplay();
    }

    setToDate()
    {
        let date = dayjs(this.toDateInput.valueAsDate).utc().hour(0);
        const time = this.toTimeInput.valueAsNumber;

        if (date != null && time != null && !isNaN(time))
            date = date.millisecond(time);

        this.rangeEnd = date;
        this.updateDisplay();
    }

    isNanOrNull(date)
    {
        return date == null || isNaN(date.unix())
    }
}

window.DatePicker = DatePicker;
