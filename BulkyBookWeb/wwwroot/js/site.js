// Please see documentation at https://docs.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Get cart product number
$(document).ready(function(){
    $.ajax({
        url: "/Customer/Home/GetCartNumber",
        type: 'GET',
        dataType: 'text', // added data type
        success: function (res) {
            $("#cart_product_number").text(res);
        },
        error: function (res) {
            $("#cart_product_number").text(0);
        }
    });
});
