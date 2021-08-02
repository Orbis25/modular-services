// Advertencia para marcar y desmarcar todos los prestamos como si estuvieran al dia */
const setAllIsUpToUpadateLoans = (e) => {
    Swal.fire({
        title: "Advertencia",
        text: "Cuidado!, va a marcar o desmarcar todos los prestamos al dia",
        icon: "warning"
    }).then((result) => {
        if (result.value) {
            $('#DisableIsUpToDateAll').submit();
        } else {
            const isCheked = $('#checkAll').is(':checked');
            if (isCheked) {
                $('#checkAll').prop("checked", false);
            } else {
                $('#checkAll').prop("checked", true);
            }
        }
    });
};

/**
 * Obtiene los prestamos saldados y lo asigna al tab de saldados
 * */
const getSoldOut = () => {
    fetch("/loan/GetAllSoldOut").then((response) => response.text()).then((result) => {
        $('#soldOutContent').empty();
        $('.loading').hide();
        $("#soldOutContent").append(result);
    });
};


/**
 * Obtiene los prestamos saldados por reenganche y lo asigna al tab de saldados  por reenganche
 * */
const getSoldOutReenclosing = () => {
    fetch("/loan/GetAllRenclosing").then((response) => response.text()).then((result) => {
        $('#RenclosingContent').empty();
        $('.loading').hide();
        $("#RenclosingContent").append(result);
    });
};

const showLoading = () => {
    $('.loading').show();
};

const postIsUpDate = (id) => {
    fetch(`/Loan/IsUpToDate?id=${id}`, { method: "POST" }).then((response) => {
        if (response.status !== 200) {
            location.reload();
        }
    }).catch(() => {
        location.reload();
    });
};


/**
 * Agrega una nota al prestamo
 * @param {any} id id del prestamo
 * @param {any} note nota
 */
const openNoteToLoan = (id, note) => {
    let html = `<textarea class='form-control' placeholder='INGRESA UNA NOTA' id='noteloan'>${note}</textArea>`;

    if (note !== "") {
        html += `<div class='row mt-4'><div class='col-12'>
                    <button onclick="addNoteToLoan('${id}')" class='btn btn-block btn-danger'>Eliminar nota</button>
                </div></div>`;
    }
    Swal.fire({
        title: "Nota",
        html: html
    }).then(({ value }) => {
        const text = $('#noteloan').val();
        if (value) {
            addNoteToLoan(id, text);
        }
    });
};


const addNoteToLoan = (id, text = "") => {
    fetch(`/loan/AddNoteToLoan?loanId=${id}&note=${text}`, { method: "POST" }).then((response) => {
        if (response.ok) {
            Swal.fire("Listo").then(() => location.reload());
        } else {
            Swal.fire("Error");
        }
    });
};