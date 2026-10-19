$(document).ready(function () {

    $("div.treeView label.hassub").click(function () {
        var visibility = $(this).parent().find('ul').css('visibility');
        if (visibility === 'visible') {
            $(this).parent().children('ul').css('visibility', 'hidden');
            $(this).parent().children('ul').css('display', 'none');
            $(this).css('background', 'url(/Images/plus.gif) no-repeat scroll 100% 50% ');
        }
        else {
            $(this).parent().children('ul').css('visibility', 'visible');
            $(this).parent().children('ul').css('display', '');
            $(this).css('background', 'url(/Images/minus.gif) no-repeat scroll 100% 50% ');
        }
    });

});
